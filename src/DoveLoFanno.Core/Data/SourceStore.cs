using DoveLoFanno.Epg;
using DoveLoFanno.Sources;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

public sealed record SourceRow(long Id, string Name, string Url, string XmltvUrl, int? MaxConnections, bool Enabled, int Priority, DateTimeOffset? LastCheckedAt, string LastError);

public sealed record ChannelRow(long Id, string Key, string Name, string Logo, string Group, bool Excluded, int Streams, int StreamsOk);

public sealed record StreamRow(long Id, long ChannelId, long SourceId, string Url, string Kind, string State, int Priority, int? Width, int? Height, string VideoCodec, string AudioCodec, string UserAgent, DateTimeOffset? LastCheckedAt);

/// <summary>Sorgenti M3U, canali e flussi nel database.</summary>
public sealed class SourceStore(string dbPath)
{
    SqliteConnection Open() => Database.Open(dbPath);

    public long AddSource(string name, string url, DateTimeOffset now)
    {
        using var c = Open();
        return Db.Insert(c, "INSERT INTO sources (name, url) VALUES ($n, $u)", ("$n", name), ("$u", url));
    }

    public List<SourceRow> Sources()
    {
        using var c = Open();
        return Db.Query(c, "SELECT id, name, url, xmltv_url, max_connections, enabled, priority, last_checked_at, last_error FROM sources ORDER BY priority, id",
            r => new SourceRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), Db.NInt(r, 4), r.GetInt64(5) == 1, r.GetInt32(6), Db.NTime(r, 7), r.GetString(8)));
    }

    public SourceRow? Source(long id) => Sources().FirstOrDefault(s => s.Id == id);

    public void UpdateSource(long id, string? name = null, int? maxConnections = null, bool clearMaxConnections = false, bool? enabled = null, int? priority = null)
    {
        using var c = Open();
        if (name != null) Db.Exec(c, "UPDATE sources SET name = $v WHERE id = $id", ("$v", name), ("$id", id));
        if (maxConnections != null || clearMaxConnections) Db.Exec(c, "UPDATE sources SET max_connections = $v WHERE id = $id", ("$v", (object?)maxConnections), ("$id", id));
        if (enabled != null) Db.Exec(c, "UPDATE sources SET enabled = $v WHERE id = $id", ("$v", enabled.Value ? 1 : 0), ("$id", id));
        if (priority != null) Db.Exec(c, "UPDATE sources SET priority = $v WHERE id = $id", ("$v", priority.Value), ("$id", id));
    }

    public void DeleteSource(long id)
    {
        using var c = Open();
        Db.Exec(c, "DELETE FROM sources WHERE id = $id", ("$id", id));
    }

    public void SetSourceError(long id, string error, DateTimeOffset now)
    {
        using var c = Open();
        Db.Exec(c, "UPDATE sources SET last_error = $e, last_checked_at = $t WHERE id = $id", ("$e", error), ("$t", Db.Unix(now)), ("$id", id));
    }

    /// <summary>Identità stabile del canale: tvg-id se c'è, altrimenti il nome normalizzato.</summary>
    public static string ChannelKey(M3uEntry e) =>
        e.TvgId.Trim().Length > 0 ? "id:" + e.TvgId.Trim().ToLowerInvariant() : "name:" + Titles.ChannelKey(e.Name);

    /// <summary>
    /// Importa la playlist letta: crea i canali nuovi, aggiunge i flussi nuovi (DRM segnati come tali), toglie quelli
    /// che la playlist non ha più. Lo stato dei flussi rimasti si conserva. Restituisce (aggiunti, tolti, DRM).
    /// </summary>
    public (int Added, int Removed, int Drm) Import(long sourceId, M3uPlaylist playlist, DateTimeOffset now)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        int added = 0, drm = 0;
        var keep = new HashSet<long>();
        foreach (var e in playlist.Entries)
        {
            string key = ChannelKey(e);
            if (key is "id:" or "name:") continue;
            long? ch = Db.ScalarLong(c, "SELECT id FROM channels WHERE key = $k", ("$k", key));
            if (ch == null)
                ch = Db.Insert(c, "INSERT INTO channels (key, name, logo, group_title) VALUES ($k, $n, $l, $g)", ("$k", key), ("$n", e.Name), ("$l", e.Logo), ("$g", e.Group));
            string kind = e.Url.Contains(".mpd", StringComparison.OrdinalIgnoreCase) ? "dash"
                : e.Url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ? "hls" : "unknown";
            long? st = Db.ScalarLong(c, "SELECT id FROM streams WHERE source_id = $s AND channel_id = $c AND url = $u", ("$s", sourceId), ("$c", ch), ("$u", e.Url));
            if (st == null)
            {
                st = Db.Insert(c, "INSERT INTO streams (channel_id, source_id, url, kind, state, user_agent, referrer) VALUES ($c, $s, $u, $k, $st, $ua, $r)",
                    ("$c", ch), ("$s", sourceId), ("$u", e.Url), ("$k", kind), ("$st", e.Drm ? StreamState.Drm : StreamState.Unchecked), ("$ua", e.UserAgent), ("$r", e.Referrer));
                added++;
            }
            else Db.Exec(c, "UPDATE streams SET user_agent = $ua, referrer = $r WHERE id = $id", ("$ua", e.UserAgent), ("$r", e.Referrer), ("$id", st));
            if (e.Drm) drm++;
            keep.Add(st.Value);
        }
        var old = Db.Query(c, "SELECT id FROM streams WHERE source_id = $s", r => r.GetInt64(0), ("$s", sourceId));
        int removed = 0;
        foreach (long id in old.Where(id => !keep.Contains(id)))
        {
            Db.Exec(c, "DELETE FROM streams WHERE id = $id", ("$id", id));
            removed++;
        }
        Db.Exec(c, "UPDATE sources SET xmltv_url = $x, last_checked_at = $t, last_error = '' WHERE id = $id",
            ("$x", playlist.EpgUrl), ("$t", Db.Unix(now)), ("$id", sourceId));
        tx.Commit();
        return (added, removed, drm);
    }

    public List<ChannelRow> Channels()
    {
        using var c = Open();
        return Db.Query(c, """
            SELECT ch.id, ch.key, ch.name, ch.logo, ch.group_title, ch.excluded,
                   count(st.id), sum(CASE WHEN st.state = 'ok' THEN 1 ELSE 0 END)
            FROM channels ch LEFT JOIN streams st ON st.channel_id = ch.id
            GROUP BY ch.id ORDER BY ch.name COLLATE NOCASE
            """,
            r => new ChannelRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt64(5) == 1, r.GetInt32(6), r.IsDBNull(7) ? 0 : r.GetInt32(7)));
    }

    public List<StreamRow> Streams(long? channelId = null, long? sourceId = null)
    {
        using var c = Open();
        return Db.Query(c, """
            SELECT id, channel_id, source_id, url, kind, state, priority, width, height, video_codec, audio_codec, user_agent, last_checked_at
            FROM streams WHERE ($c IS NULL OR channel_id = $c) AND ($s IS NULL OR source_id = $s) ORDER BY channel_id, priority, id
            """,
            r => new StreamRow(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt32(6),
                Db.NInt(r, 7), Db.NInt(r, 8), r.GetString(9), r.GetString(10), r.GetString(11), Db.NTime(r, 12)),
            ("$c", channelId), ("$s", sourceId));
    }

    /// <summary>Flussi del canale dalle sorgenti attive, con i dati per <see cref="StreamSelector"/>.</summary>
    public List<StreamCandidate> Candidates(long channelId)
    {
        using var c = Open();
        return Db.Query(c, """
            SELECT st.id, st.source_id, st.url, CASE WHEN st.user_agent <> '' THEN st.user_agent ELSE so.user_agent END,
                   st.state, st.priority, so.priority, so.max_connections, st.referrer
            FROM streams st JOIN sources so ON so.id = st.source_id
            WHERE st.channel_id = $c AND so.enabled = 1
            """,
            r => new StreamCandidate(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(8), r.GetString(4), r.GetInt32(5), r.GetInt32(6), Db.NInt(r, 7)),
            ("$c", channelId));
    }

    public void SaveProbe(long streamId, ProbeResult p, DateTimeOffset now)
    {
        using var c = Open();
        Db.Exec(c, """
            UPDATE streams SET state = $st, kind = CASE WHEN $k = 'unknown' THEN kind ELSE $k END, width = $w, height = $h,
                   video_codec = $v, audio_codec = $a, last_checked_at = $t WHERE id = $id
            """,
            ("$st", p.State), ("$k", p.Kind), ("$w", p.Width), ("$h", p.Height), ("$v", p.VideoCodec), ("$a", p.AudioCodec), ("$t", Db.Unix(now)), ("$id", streamId));
    }

    public void SetStreamPriority(long streamId, int priority)
    {
        using var c = Open();
        Db.Exec(c, "UPDATE streams SET priority = $p WHERE id = $id", ("$p", priority), ("$id", streamId));
    }
}
