using DoveLoFanno.Epg;
using DoveLoFanno.Sources;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

public sealed record SourceRow(long Id, string Name, string Url, string XmltvUrl, int? MaxConnections, bool Enabled, int Priority, DateTimeOffset? LastCheckedAt, string LastError);

/// <param name="Number">Numero del digitale terrestre (LCN nazionale), null se il canale non ne ha uno: solo un'etichetta.</param>
/// <param name="Order">Posto del canale negli elenchi (0 = primo): l'ordine della playlist, come in RegistraTV.</param>
/// <param name="Region">regione dal nome, "" se non si capisce (<see cref="ChannelInfo"/>, 23/11/2026)</param>
/// <param name="Kind">tipo del canale: radio, news, sports, kids, music, religious, shop, movies, culture, entertainment, general, ""</param>
/// <param name="Closed">chiuso secondo il database iptv-org</param>
/// <param name="GuideShow">scelta a mano per la guida: null = automatica (filtro «Canali»), true = sempre, false = mai</param>
public sealed record ChannelRow(long Id, string Key, string Name, string Logo, string Group, bool Excluded, int Streams, int StreamsOk,
    int? Number = null, int Order = 0, string Region = "", string Kind = "", bool Closed = false, bool? GuideShow = null);

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

    /// <summary>
    /// Toglie la playlist con i suoi flussi e i canali che restano senza flussi (20/11/2026, scelta di Antonio: togliendo
    /// «forgejo Italia» restavano 317 canali vuoti). I canali con registrazioni (fatte o programmate) restano, per lo
    /// storico. Restituisce quanti canali ha tolto.
    /// </summary>
    public int DeleteSource(long id)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var mine = Db.Query(c, "SELECT DISTINCT channel_id FROM streams WHERE source_id = $id", r => r.GetInt64(0), ("$id", id));
        Db.Exec(c, "DELETE FROM sources WHERE id = $id", ("$id", id));
        int removed = 0;
        foreach (long ch in mine)
            removed += Db.Exec(c, """
                DELETE FROM channels WHERE id = $c
                  AND NOT EXISTS (SELECT 1 FROM streams WHERE channel_id = $c)
                  AND NOT EXISTS (SELECT 1 FROM recordings WHERE channel_id = $c)
                """, ("$c", ch));
        tx.Commit();
        return removed;
    }

    public void SetSourceError(long id, string error, DateTimeOffset now)
    {
        using var c = Open();
        Db.Exec(c, "UPDATE sources SET last_error = $e, last_checked_at = $t WHERE id = $id", ("$e", error), ("$t", Db.Unix(now)), ("$id", id));
    }

    /// <summary>
    /// Identità stabile del canale: tvg-id se c'è, altrimenti il nome, tutti e due normalizzati come per la guida
    /// (Titles.ChannelKey): "rai1.it" e "Rai.1.HD..101.it" sono lo stesso canale (19/11/2026: una seconda playlist
    /// aveva creato un secondo «Rai 1» invece di un flusso di riserva, e la griglia della guida si bloccava).
    /// </summary>
    public static string ChannelKey(M3uEntry e)
    {
        string id = e.TvgId.Trim();
        if (id.Length == 0) return "name:" + Titles.ChannelKey(e.Name);
        string n = Titles.ChannelKey(id);
        return "id:" + (n.Length > 0 ? n : id.ToLowerInvariant());
    }

    /// <summary>La parte che decide se due canali sono lo stesso: tvg-id o nome normalizzati, senza "id:"/"name:".</summary>
    static string SameChannel(string key)
    {
        int colon = key.IndexOf(':');
        string rest = colon >= 0 ? key[(colon + 1)..] : key;
        string n = Titles.ChannelKey(rest);
        return n.Length > 0 ? n : rest;
    }

    /// <summary>Nome del canale normalizzato ("Rai 1 HD" = "Rai 1"); vuoto se non resta niente.</summary>
    static string SameName(string name) => Titles.ChannelKey(name);

    /// <summary>
    /// Numero nazionale del digitale terrestre come terza prova che due canali sono lo stesso (23/11/2026): una seconda
    /// playlist aveva «Cielo TV» (tvg-id cielo) accanto a «Cielo» (tvg-id cielotv) e «27 Twentyseven» accanto a «Twenty
    /// Seven», che non si univano e restavano senza guida. "" se il canale non ha un numero nazionale.
    /// </summary>
    static string SameNumber(string key, string name) =>
        DoveLoFanno.Sources.ChannelNumbers.For(key, name) is int n ? "#" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";

    /// <summary>
    /// Unisce i canali che sono lo stesso canale: stessa chiave normalizzata (anche uno per tvg-id e uno per nome) oppure
    /// stesso nome normalizzato (due playlist con tvg-id diversi, "20" e "Mediaset20.it", per «20 Mediaset»). Resta il
    /// più vecchio, che prende flussi e registrazioni degli altri. Restituisce quanti canali ha tolto.
    /// </summary>
    public int MergeDuplicateChannels()
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        int n = MergeDuplicates(c);
        tx.Commit();
        return n;
    }

    static int MergeDuplicates(SqliteConnection c)
    {
        var all = Db.Query(c, "SELECT id, key, guide_channel_id, name FROM channels ORDER BY id",
            r => (Id: r.GetInt64(0), Key: r.GetString(1), Guide: Db.NLong(r, 2), Name: r.GetString(3)));
        // gruppi: due canali stanno insieme se hanno in comune la chiave o il nome normalizzati o il numero nazionale (anche
        // a catena)
        var parent = all.ToDictionary(x => x.Id, x => x.Id);
        long Find(long x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        var firstByKey = new Dictionary<string, long>();
        foreach (var x in all)
            foreach (string k in new[] { "k:" + SameChannel(x.Key), "n:" + SameName(x.Name), SameNumber(x.Key, x.Name) })
            {
                if (k is "k:" or "n:" or "") continue;
                if (firstByKey.TryGetValue(k, out long other)) { long a = Find(x.Id), b = Find(other); if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b); }
                else firstByKey[k] = x.Id;
            }
        int removed = 0;
        foreach (var g in all.GroupBy(x => Find(x.Id)))
        {
            var keep = g.First();
            foreach (var dup in g.Skip(1))
            {
                // un flusso con lo stesso indirizzo nella stessa playlist c'è già nel canale che resta: quello doppio si toglie
                Db.Exec(c, "UPDATE OR IGNORE streams SET channel_id = $k WHERE channel_id = $d", ("$k", keep.Id), ("$d", dup.Id));
                Db.Exec(c, "DELETE FROM streams WHERE channel_id = $d", ("$d", dup.Id));
                Db.Exec(c, "UPDATE recordings SET channel_id = $k WHERE channel_id = $d", ("$k", keep.Id), ("$d", dup.Id));
                if (keep.Guide == null && dup.Guide != null)
                    Db.Exec(c, "UPDATE channels SET guide_channel_id = $g WHERE id = $k", ("$g", dup.Guide), ("$k", keep.Id));
                Db.Exec(c, "DELETE FROM channels WHERE id = $d", ("$d", dup.Id));
                removed++;
            }
            // la chiave del canale che resta nella forma normalizzata (nessun altro canale può averla: sarebbe nel gruppo)
            string key = (keep.Key.StartsWith("id:", StringComparison.Ordinal) ? "id:" : "name:") + SameChannel(keep.Key);
            if (key != keep.Key) Db.Exec(c, "UPDATE channels SET key = $key WHERE id = $id", ("$key", key), ("$id", keep.Id));
        }
        return removed;
    }

    /// <summary>
    /// Importa la playlist letta: crea i canali nuovi, aggiunge i flussi nuovi (DRM segnati come tali), toglie quelli
    /// che la playlist non ha più. Lo stato dei flussi rimasti si conserva. Restituisce (aggiunti, tolti, DRM).
    /// </summary>
    public (int Added, int Removed, int Drm) Import(long sourceId, M3uPlaylist playlist, DateTimeOffset now)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        int added = 0, drm = 0, position = -1;
        var keep = new HashSet<long>();
        MergeDuplicates(c);
        // canali già noti per chiave o nome normalizzati: lo stesso canale in un'altra playlist, con il tvg-id scritto in
        // un altro modo o diverso, diventa un flusso di riserva e non un secondo canale
        var known = new Dictionary<string, long>();
        foreach (var (id, k, n) in Db.Query(c, "SELECT id, key, name FROM channels ORDER BY id", r => (r.GetInt64(0), r.GetString(1), r.GetString(2))))
        {
            known.TryAdd("k:" + SameChannel(k), id);
            if (SameName(n).Length > 0) known.TryAdd("n:" + SameName(n), id);
            if (SameNumber(k, n) is { Length: > 0 } num) known.TryAdd(num, id);
        }
        foreach (var e in playlist.Entries)
        {
            position++;
            string key = ChannelKey(e);
            if (key is "id:" or "name:") continue;
            string byName = SameName(e.Name);
            string byNumber = SameNumber(key, e.Name);
            long? ch = known.TryGetValue("k:" + SameChannel(key), out long found) ? found
                : byName.Length > 0 && known.TryGetValue("n:" + byName, out found) ? found
                : byNumber.Length > 0 && known.TryGetValue(byNumber, out found) ? found : null;
            if (ch == null)
            {
                ch = Db.Insert(c, "INSERT INTO channels (key, name, logo, group_title) VALUES ($k, $n, $l, $g)", ("$k", key), ("$n", e.Name), ("$l", e.Logo), ("$g", e.Group));
                known["k:" + SameChannel(key)] = ch.Value;
                if (byName.Length > 0) known.TryAdd("n:" + byName, ch.Value);
                if (byNumber.Length > 0) known.TryAdd(byNumber, ch.Value);
            }
            string kind = e.Url.Contains(".mpd", StringComparison.OrdinalIgnoreCase) ? "dash"
                : e.Url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ? "hls" : "unknown";
            long? st = Db.ScalarLong(c, "SELECT id FROM streams WHERE source_id = $s AND channel_id = $c AND url = $u", ("$s", sourceId), ("$c", ch), ("$u", e.Url));
            if (st == null)
            {
                st = Db.Insert(c, "INSERT INTO streams (channel_id, source_id, url, kind, state, user_agent, referrer, position) VALUES ($c, $s, $u, $k, $st, $ua, $r, $p)",
                    ("$c", ch), ("$s", sourceId), ("$u", e.Url), ("$k", kind), ("$st", e.Drm ? StreamState.Drm : StreamState.Unchecked), ("$ua", e.UserAgent), ("$r", e.Referrer), ("$p", position));
                added++;
            }
            else Db.Exec(c, "UPDATE streams SET user_agent = $ua, referrer = $r, position = $p WHERE id = $id", ("$ua", e.UserAgent), ("$r", e.Referrer), ("$p", position), ("$id", st));
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

    /// <summary>
    /// Canali nell'ordine della playlist, come RegistraTV: prima la playlist principale (priorità più bassa), nella sua
    /// sequenza; un canale in più playlist prende il posto migliore. Poi i canali che nessuna playlist attiva ha più,
    /// per numero del digitale terrestre e nome.
    /// </summary>
    static ChannelRow WithFacts(ChannelRow r)
    {
        var f = ChannelInfo.For(r.Key, r.Name, r.Group);
        return r with { Region = f.Region, Kind = f.Kind, Closed = f.Closed };
    }

    /// <summary>Scelta a mano di un canale nella guida (null = automatica). False se il canale non c'è.</summary>
    public bool SetGuideShow(long channelId, bool? show)
    {
        using var c = Open();
        return Db.Exec(c, "UPDATE channels SET guide_show = $v WHERE id = $id", ("$v", show is bool b ? (b ? 1 : 0) : null), ("$id", channelId)) == 1;
    }

    public List<ChannelRow> Channels()
    {
        using var c = Open();
        return QueryChannels(c);
    }

    internal static List<ChannelRow> QueryChannels(SqliteConnection c)
    {
        var rows = Db.Query(c, """
            SELECT ch.id, ch.key, ch.name, ch.logo, ch.group_title, ch.excluded,
                   count(st.id), sum(CASE WHEN st.state = 'ok' THEN 1 ELSE 0 END),
                   min(CASE WHEN so.id IS NOT NULL THEN so.priority * 1000000 + st.position END), ch.guide_show
            FROM channels ch LEFT JOIN streams st ON st.channel_id = ch.id
            LEFT JOIN sources so ON so.id = st.source_id AND so.enabled = 1
            GROUP BY ch.id
            """,
            r => (Row: WithFacts(new ChannelRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt64(5) == 1, r.GetInt32(6),
                    r.IsDBNull(7) ? 0 : r.GetInt32(7), ChannelNumbers.For(r.GetString(1), r.GetString(2)),
                    GuideShow: r.IsDBNull(9) ? null : r.GetInt64(9) == 1)),
                  Pos: r.IsDBNull(8) ? (long?)null : r.GetInt64(8)));
        rows.Sort((a, b) =>
            a.Pos != b.Pos && a.Pos != null && b.Pos != null ? a.Pos.Value.CompareTo(b.Pos.Value)
            : a.Pos == null != (b.Pos == null) ? (a.Pos == null ? 1 : -1)
            : ChannelNumbers.Compare(a.Row.Number, a.Row.Name, b.Row.Number, b.Row.Name));
        return rows.Select((x, i) => x.Row with { Order = i }).ToList();
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
