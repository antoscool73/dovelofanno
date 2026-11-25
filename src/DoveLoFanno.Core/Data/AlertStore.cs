using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

/// <summary>
/// Un avviso del profilo su un'opera. Kind: tv (in chiaro in TV), subscription (nei miei abbonamenti), rent_buy (a noleggio
/// o acquisto). Channels: all o playlist; Moments: appear (appena compare in guida), eve (la sera prima, dalle 21), soon
/// (30 minuti prima); Services: piattaforme da controllare (vuoto = gli abbonamenti del profilo); Offers: rent, buy.
/// </summary>
public sealed record AlertRow(
    long Id, long WorkId, string WorkTitle, int WorkYear, string WorkKind, string Kind, bool Active,
    string Channels, bool AutoRecord, IReadOnlyList<string> Moments, IReadOnlyList<string> Services, IReadOnlyList<string> Offers,
    DateTimeOffset? LastFiredAt, string Status, string Poster = "");

/// <summary>Campi di un avviso; quelli mancanti prendono il valore predefinito.</summary>
public sealed record AlertBody(long WorkId, string Kind, bool? Active, string? Channels, bool? AutoRecord,
    IReadOnlyList<string>? Moments, IReadOnlyList<string>? Services, IReadOnlyList<string>? Offers);

/// <summary>Quello che è successo in un giro di controllo degli avvisi.</summary>
public sealed record AlertFired(long ProfileId, long WorkId, string Title, string Text, string Link, long? RecordingId)
{
    // per unire gli scatti della stessa opera in una notifica sola (25/11/2026)
    internal string WorkTitle { get; init; } = "";
    /// <summary>Passaggio in TV: la riga «Domani alle 21:20 su Rai 1».</summary>
    internal string Line { get; init; } = "";
    /// <summary>On demand: piattaforma e modo («in abbonamento», «a noleggio»…).</summary>
    internal string Platform { get; init; } = "";
    internal string How { get; init; } = "";
}

/// <summary>
/// Avvisi: tre indipendenti per opera e profilo (in chiaro in TV, nei miei abbonamenti, a noleggio o acquisto). Ogni scatto
/// si annota in alert_events con la sua chiave (passaggio o piattaforma), così un avviso scatta una volta sola.
/// </summary>
public sealed class AlertStore(string dbPath, GuideStore guide, SubscriptionStore subscriptions)
{
    public static readonly string[] Kinds = ["tv", "subscription", "rent_buy"];
    static readonly string[] AllMoments = ["appear", "eve", "soon"];

    sealed record Options(string Channels, bool AutoRecord, List<string> Moments, List<string> Services, List<string> Offers);

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    SqliteConnection Open() => Database.Open(dbPath);

    static Options Defaults(string kind) => new("all", kind == "tv", kind == "tv" ? ["appear", "eve"] : [], [], kind == "rent_buy" ? ["rent", "buy"] : []);

    static Options Parse(string json, string kind)
    {
        try { return JsonSerializer.Deserialize<Options>(json, Json) ?? Defaults(kind); }
        catch (JsonException) { return Defaults(kind); }
    }

    /// <summary>Avvisi del profilo (di un'opera sola se indicata); null se il profilo non esiste.</summary>
    public List<AlertRow>? List(long profileId, long? workId, DateTimeOffset now)
    {
        List<(long Id, long Work, string Title, int Year, string WKind, string Kind, bool Active, string Opt, DateTimeOffset? Last, string Poster)> rows;
        using (var c = Open())
        {
            if (Db.ScalarLong(c, "SELECT id FROM profiles WHERE id = $p", ("$p", profileId)) == null) return null;
            rows = Db.Query(c, """
                SELECT a.id, a.work_id, w.title, w.year, w.kind, a.kind, a.active, a.options, a.last_fired_at, COALESCE(w.poster, '')
                FROM alerts a JOIN works w ON w.id = a.work_id WHERE a.profile_id = $p AND ($w IS NULL OR a.work_id = $w)
                ORDER BY w.title COLLATE NOCASE, a.kind
                """,
                r => (r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt32(3), r.GetString(4), r.GetString(5), r.GetInt64(6) == 1, r.GetString(7), Db.NTime(r, 8), r.GetString(9)),
                ("$p", profileId), ("$w", workId));
        }
        return rows.Select(x =>
        {
            var o = Parse(x.Opt, x.Kind);
            return new AlertRow(x.Id, x.Work, x.Title, x.Year, x.WKind, x.Kind, x.Active, o.Channels, o.AutoRecord, o.Moments, o.Services, o.Offers,
                x.Last, Status(profileId, x.Work, x.Kind, o, now), WorkStore.ImageUrl(x.Poster, "w342"));
        }).ToList();
    }

    /// <summary>Messaggio d'errore in italiano, o vuoto se l'avviso va bene.</summary>
    public static string Validate(AlertBody b)
    {
        if (!Kinds.Contains(b.Kind)) return "tipo di avviso: tv, subscription o rent_buy";
        if (b.Channels is not (null or "all" or "playlist")) return "canali: all o playlist";
        if (b.Moments?.Any(m => !AllMoments.Contains(m)) == true) return "momenti: appear, eve, soon";
        if (b.Offers?.Any(m => m is not ("rent" or "buy")) == true) return "offerte: rent o buy";
        return "";
    }

    /// <summary>Crea o aggiorna l'avviso dell'opera per quel tipo. Id, oppure null se profilo o opera non esistono.</summary>
    public long? Save(long profileId, AlertBody b)
    {
        using var c = Open();
        if (Db.ScalarLong(c, "SELECT id FROM profiles WHERE id = $p", ("$p", profileId)) == null) return null;
        if (Db.ScalarLong(c, "SELECT id FROM works WHERE id = $w", ("$w", b.WorkId)) == null) return null;
        var cur = Db.Query(c, "SELECT id, options, active FROM alerts WHERE profile_id = $p AND work_id = $w AND kind = $k",
            r => (Id: r.GetInt64(0), Opt: r.GetString(1), Active: r.GetInt64(2) == 1), ("$p", profileId), ("$w", b.WorkId), ("$k", b.Kind));
        var o = cur.Count > 0 ? Parse(cur[0].Opt, b.Kind) : Defaults(b.Kind);
        o = new Options(b.Channels ?? o.Channels, b.AutoRecord ?? o.AutoRecord, b.Moments?.Distinct().ToList() ?? o.Moments,
            b.Services?.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? o.Services,
            b.Offers?.Distinct().ToList() ?? o.Offers);
        bool active = b.Active ?? (cur.Count == 0 || cur[0].Active);
        string json = JsonSerializer.Serialize(o, Json);
        if (cur.Count > 0)
        {
            Db.Exec(c, "UPDATE alerts SET options = $o, active = $a WHERE id = $id", ("$o", json), ("$a", active ? 1 : 0), ("$id", cur[0].Id));
            return cur[0].Id;
        }
        return Db.Insert(c, "INSERT INTO alerts (profile_id, work_id, kind, active, options) VALUES ($p, $w, $k, $a, $o)",
            ("$p", profileId), ("$w", b.WorkId), ("$k", b.Kind), ("$a", active ? 1 : 0), ("$o", json));
    }

    public bool Delete(long profileId, long id)
    {
        using var c = Open();
        return Db.Exec(c, "DELETE FROM alerts WHERE id = $id AND profile_id = $p", ("$id", id), ("$p", profileId)) > 0;
    }

    /// <summary>Opere con un avviso attivo su abbonamenti o noleggio: le loro piattaforme si aggiornano una volta al giorno.</summary>
    public List<long> WorksToWatchOnline()
    {
        using var c = Open();
        return Db.Query(c, "SELECT DISTINCT work_id FROM alerts WHERE active = 1 AND kind IN ('subscription', 'rent_buy')", r => r.GetInt64(0));
    }

    // ---------------------------------------------------------------- controllo

    /// <summary>
    /// Un giro di controllo: restituisce gli avvisi scattati adesso (una volta sola per chiave). <paramref name="schedule"/>
    /// programma una registrazione automatica (canale, programma, profilo) e ne restituisce l'id, o null se non riesce.
    /// </summary>
    public List<AlertFired> Check(DateTimeOffset now, TimeZoneInfo zone, Func<GuideProgramme, long, long?> schedule)
    {
        List<(long Id, long Profile, long Work, string Title, string Kind, string Opt)> alerts;
        using (var c = Open())
            alerts = Db.Query(c, """
                SELECT a.id, a.profile_id, a.work_id, w.title, a.kind, a.options FROM alerts a JOIN works w ON w.id = a.work_id WHERE a.active = 1
                """, r => (r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetString(5)));

        var fired = new List<AlertFired>();
        foreach (var a in alerts)
        {
            var o = Parse(a.Opt, a.Kind);
            string link = $"/opera/{a.Work}";
            if (a.Kind == "tv")
            {
                var airings = Airings(a.Work, now).Where(p => o.Channels == "all" || p.ChannelId != null).ToList();
                foreach (var p in airings)
                {
                    string where = $"{When(p.Start, now, zone)} su {(p.ChannelName.Length > 0 ? p.ChannelName : p.GuideChannelName)}";
                    string pk = $"{p.GuideChannelId}:{Db.Unix(p.Start)}";
                    var local = TimeZoneInfo.ConvertTime(p.Start, zone);
                    var eveFrom = new DateTimeOffset(local.Date.AddDays(-1).AddHours(21), zone.GetUtcOffset(local.Date.AddDays(-1).AddHours(21)));
                    string line = char.ToUpperInvariant(where[0]) + where[1..];
                    if (o.Moments.Contains("appear") && p.Start > now && Fire(a.Id, "appear:" + pk, now))
                        fired.Add(new AlertFired(a.Profile, a.Work, $"«{a.Title}» passa in TV", where, link, null) { WorkTitle = a.Title, Line = line });
                    // la sera prima: solo per i passaggi di un giorno successivo, dalle 21 del giorno prima
                    bool laterDay = TimeZoneInfo.ConvertTime(now, zone).Date < local.Date;
                    if (o.Moments.Contains("eve") && laterDay && now >= eveFrom && Fire(a.Id, "eve:" + pk, now))
                        fired.Add(new AlertFired(a.Profile, a.Work, $"Domani: «{a.Title}»", where, link, null) { WorkTitle = a.Title, Line = line });
                    if (o.Moments.Contains("soon") && now >= p.Start.AddMinutes(-30) && now < p.Start && Fire(a.Id, "soon:" + pk, now))
                        fired.Add(new AlertFired(a.Profile, a.Work, $"Tra poco: «{a.Title}»", where, link, null) { WorkTitle = a.Title, Line = "Tra poco: " + where });
                }
                // registrazione automatica: il primo passaggio registrabile, se l'opera non è già in casa né in programma
                if (o.AutoRecord && airings.FirstOrDefault(p => p.ChannelId != null && p.Stop > now) is { } first
                    && !airings.Any(p => p.Scheduled) && !InHouseOrPending(a.Work)
                    && !HasEvent(a.Id, $"rec:{first.GuideChannelId}:{Db.Unix(first.Start)}"))
                {
                    long? rec = schedule(first, a.Profile);
                    if (rec != null && Fire(a.Id, $"rec:{first.GuideChannelId}:{Db.Unix(first.Start)}", now))
                        fired.Add(new AlertFired(a.Profile, a.Work, $"Registrazione programmata: «{a.Title}»",
                            $"{When(first.Start, now, zone)} su {first.ChannelName}, da un tuo avviso", "/registrazioni", rec));
                }
            }
            else
            {
                var services = a.Kind == "subscription" ? Services(a.Profile, o) : [];
                foreach (var (platform, kind) in Availability(a.Work))
                {
                    bool match = a.Kind == "subscription"
                        ? kind is "subscription" or "free" && (services.Count == 0 || services.Contains(platform, StringComparer.OrdinalIgnoreCase))
                        : o.Offers.Contains(kind);
                    if (!match || !Fire(a.Id, $"{kind}:{platform}", now)) continue;
                    string how = kind switch { "free" => "gratis", "subscription" => "in abbonamento", "rent" => "a noleggio", _ => "in vendita" };
                    fired.Add(new AlertFired(a.Profile, a.Work, $"«{a.Title}» è su {platform}", $"{char.ToUpperInvariant(how[0])}{how[1..]} su {platform}", link, null)
                        { WorkTitle = a.Title, Platform = platform, How = how });
                }
            }
        }
        return Merge(fired);
    }

    /// <summary>
    /// Una notifica sola per opera e profilo in ogni giro (25/11/2026, richiesta di Antonio: «Le ali della libertà» ne aveva
    /// mandate 9, una per piattaforma e modo). Prima i passaggi in TV, poi le piattaforme, ognuna su una riga con i suoi modi
    /// («Su Amazon Video a noleggio o in vendita»). Le registrazioni automatiche restano a parte.
    /// </summary>
    static List<AlertFired> Merge(List<AlertFired> fired)
    {
        static int Rank(string how) => how switch { "gratis" => 0, "in abbonamento" => 1, "a noleggio" => 2, _ => 3 };
        var result = new List<AlertFired>();
        foreach (var g in fired.Where(f => f.RecordingId == null).GroupBy(f => (f.ProfileId, f.WorkId)))
        {
            var items = g.ToList();
            if (items.Count == 1) { result.Add(items[0]); continue; }
            var first = items[0];
            var tv = items.Where(f => f.Line.Length > 0).Select(f => f.Line).Distinct().ToList();
            var online = items.Where(f => f.Platform.Length > 0).GroupBy(f => f.Platform, StringComparer.OrdinalIgnoreCase)
                .Select(p => (Platform: p.First().Platform, Hows: p.Select(f => f.How).Distinct().OrderBy(Rank).ToList()))
                .OrderBy(p => Rank(p.Hows[0])).ThenBy(p => p.Platform, StringComparer.OrdinalIgnoreCase).ToList();
            var lines = tv.Concat(online.Select(p => $"Su {p.Platform} {string.Join(" o ", p.Hows)}")).ToList();
            string title = online.Count == 0 ? (items.All(f => f.Title == first.Title) ? first.Title : $"«{first.WorkTitle}» in TV")
                : tv.Count > 0 ? $"«{first.WorkTitle}»: in TV e on demand"
                : online.Count == 1 ? $"«{first.WorkTitle}» è su {online[0].Platform}"
                : online.Count == 2 ? $"«{first.WorkTitle}» è su {online[0].Platform} e {online[1].Platform}"
                : $"«{first.WorkTitle}» è su {online[0].Platform}, {online[1].Platform} e altre {online.Count - 2}";
            result.Add(new AlertFired(first.ProfileId, first.WorkId, title, string.Join("\n", lines), first.Link, null) { WorkTitle = first.WorkTitle });
        }
        result.AddRange(fired.Where(f => f.RecordingId != null));
        return result;
    }

    List<string> Services(long profileId, Options o) =>
        o.Services.Count > 0 ? o.Services : subscriptions.OfProfile(profileId)?.Services.ToList() ?? [];

    /// <summary>Passaggi futuri dell'opera; per i film anche quelli con lo stesso titolo e anno 0 (Mediaset non dà l'anno).</summary>
    List<GuideProgramme> Airings(long workId, DateTimeOffset now)
    {
        List<long> ids;
        using (var c = Open())
            ids = Db.Query(c, """
                SELECT p.id FROM programmes p JOIN works w ON w.id = p.work_id JOIN works t ON t.id = $w
                WHERE p.stop > $now AND (w.id = t.id OR (t.kind = 'film' AND w.kind = 'film' AND w.title_key = t.title_key AND (w.year = 0 OR t.year = 0)))
                ORDER BY p.start LIMIT 30
                """, r => r.GetInt64(0), ("$w", workId), ("$now", Db.Unix(now)));
        return ids.Select(guide.Programme).Where(p => p != null).Select(p => p!).ToList();
    }

    List<(string Platform, string Kind)> Availability(long workId)
    {
        using var c = Open();
        return Db.Query(c, "SELECT platform, kind FROM availability WHERE work_id = $w", r => (r.GetString(0), r.GetString(1)), ("$w", workId));
    }

    bool InHouseOrPending(long workId)
    {
        using var c = Open();
        return (Db.ScalarLong(c, """
            SELECT (SELECT count(*) FROM recordings WHERE work_id = $w AND state IN ('scheduled', 'recording', 'merging', 'completed'))
                 + (SELECT count(*) FROM library_items WHERE work_id = $w AND missing_since IS NULL)
            """, ("$w", workId)) ?? 0) > 0;
    }

    bool HasEvent(long alertId, string key)
    {
        using var c = Open();
        return Db.ScalarLong(c, "SELECT 1 FROM alert_events WHERE alert_id = $a AND key = $k", ("$a", alertId), ("$k", key)) != null;
    }

    bool Fire(long alertId, string key, DateTimeOffset now)
    {
        using var c = Open();
        if (Db.Exec(c, "INSERT OR IGNORE INTO alert_events (alert_id, key, fired_at) VALUES ($a, $k, $t)", ("$a", alertId), ("$k", key), ("$t", Db.Unix(now))) == 0)
            return false;
        Db.Exec(c, "UPDATE alerts SET last_fired_at = $t WHERE id = $a", ("$t", Db.Unix(now)), ("$a", alertId));
        return true;
    }

    /// <summary>Riga di stato per l'elenco degli avvisi: il prossimo passaggio, o dove si vede già.</summary>
    string Status(long profileId, long workId, string kind, Options o, DateTimeOffset now)
    {
        if (kind == "tv")
        {
            var next = Airings(workId, now).FirstOrDefault(p => o.Channels == "all" || p.ChannelId != null);
            return next == null ? "Nessun passaggio nella guida per ora"
                : $"Prossimo passaggio: {next.Start.ToLocalTime().ToString("ddd d MMM HH:mm", CultureInfo.GetCultureInfo("it-IT"))} su {(next.ChannelName.Length > 0 ? next.ChannelName : next.GuideChannelName)}";
        }
        var av = Availability(workId);
        var hits = kind == "subscription"
            ? av.Where(x => x.Kind is "subscription" or "free" && (Services(profileId, o) is var s && (s.Count == 0 || s.Contains(x.Platform, StringComparer.OrdinalIgnoreCase)))).Select(x => x.Platform)
            : av.Where(x => o.Offers.Contains(x.Kind)).Select(x => x.Platform);
        var list = hits.Distinct().ToList();
        return list.Count == 0 ? "Per ora non c'è" : "Adesso su " + string.Join(", ", list);
    }

    static string When(DateTimeOffset start, DateTimeOffset now, TimeZoneInfo zone)
    {
        var it = CultureInfo.GetCultureInfo("it-IT");
        var s = TimeZoneInfo.ConvertTime(start, zone);
        var n = TimeZoneInfo.ConvertTime(now, zone);
        int days = (s.Date - n.Date).Days;
        string day = days switch { 0 => "oggi", 1 => "domani", _ => s.ToString("dddd d MMMM", it) };
        return $"{day} alle {s:HH:mm}";
    }
}
