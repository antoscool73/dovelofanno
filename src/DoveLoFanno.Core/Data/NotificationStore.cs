using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

/// <summary>Una notifica dell'app; ProfileId null = per tutta la famiglia.</summary>
public sealed record NotificationRow(long Id, long? ProfileId, string Kind, string Title, string Text, string Link, DateTimeOffset CreatedAt, bool Read);

public sealed record NotificationList(int Unread, IReadOnlyList<NotificationRow> Items);

/// <summary>
/// Notifiche dentro l'app, come gli avvisi dell'area di notifica di RegistraTV: registrazioni finite, avvisi scattati,
/// proposte nuove. Si tengono 30 giorni.
/// </summary>
public sealed class NotificationStore(string dbPath)
{
    SqliteConnection Open() => Database.Open(dbPath);

    /// <summary>Evento per chi le inoltra fuori dall'app (Telegram), con l'opera della notifica se c'è (per locandina e scheda).</summary>
    public event Action<NotificationRow, long?>? Added;

    /// <param name="workId">Opera di cui parla la notifica: non si salva, va solo a chi la inoltra.</param>
    public long Add(long? profileId, string kind, string title, string text, string link, DateTimeOffset now, long? workId = null)
    {
        long id;
        using (var c = Open())
        {
            id = Db.Insert(c, "INSERT INTO notifications (profile_id, kind, title, text, link, created_at) VALUES ($p, $k, $t, $x, $l, $now)",
                ("$p", profileId), ("$k", kind), ("$t", title), ("$x", text), ("$l", link), ("$now", Db.Unix(now)));
            Db.Exec(c, "DELETE FROM notifications WHERE created_at < $old", ("$old", Db.Unix(now.AddDays(-30))));
        }
        Added?.Invoke(new NotificationRow(id, profileId, kind, title, text, link, now, false), workId);
        return id;
    }

    /// <summary>Le ultime notifiche del profilo (sue e della famiglia); null se il profilo non esiste.</summary>
    /// <param name="hidden">tipi che il profilo non vuole nella campanella (NotifyPrefs, 26/11/2026)</param>
    public NotificationList? List(long profileId, int max = 50, IReadOnlySet<string>? hidden = null)
    {
        string h = "," + string.Join(",", hidden ?? new HashSet<string>()) + ",";
        using var c = Open();
        long? read = Db.ScalarLong(c, "SELECT notifications_read FROM profiles WHERE id = $p", ("$p", profileId));
        if (read == null) return null;
        var items = Db.Query(c, """
            SELECT id, profile_id, kind, title, text, link, created_at FROM notifications
            WHERE (profile_id IS NULL OR profile_id = $p) AND instr($h, ',' || kind || ',') = 0 ORDER BY id DESC LIMIT $max
            """,
            r => new NotificationRow(r.GetInt64(0), Db.NLong(r, 1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                Db.FromUnix(r.GetInt64(6)), r.GetInt64(0) <= read),
            ("$p", profileId), ("$max", max), ("$h", h));
        int unread = (int)(Db.ScalarLong(c, "SELECT count(*) FROM notifications WHERE (profile_id IS NULL OR profile_id = $p) AND id > $r AND instr($h, ',' || kind || ',') = 0",
            ("$p", profileId), ("$r", read), ("$h", h)) ?? 0);
        return new NotificationList(unread, items);
    }

    /// <summary>Segna come lette tutte le notifiche fino a <paramref name="upTo"/> compresa.</summary>
    public bool MarkRead(long profileId, long upTo)
    {
        using var c = Open();
        return Db.Exec(c, "UPDATE profiles SET notifications_read = max(notifications_read, $u) WHERE id = $p", ("$u", upTo), ("$p", profileId)) > 0;
    }
}
