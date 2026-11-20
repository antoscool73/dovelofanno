using System.Text.RegularExpressions;

namespace DoveLoFanno.Data;

/// <summary>
/// Canali a pagamento e passaggi che un profilo vede: una regola sola per guida, ricerca, liste e la mia serata
/// (20/11/2026, prima stava solo nella pagina, in pay.ts). Un canale delle playlist o con il numero del digitale
/// terrestre è in chiaro; tra gli altri si riconoscono dal nome quelli delle piattaforme a pagamento note.
/// Un passaggio si vede se il canale è in chiaro o se la sua piattaforma è tra gli abbonamenti del profilo.
/// </summary>
public static partial class PayChannels
{
    /// <summary>Piattaforma a pagamento del canale, "" se il canale è in chiaro (o non riconosciuto come a pagamento).</summary>
    public static string Platform(long? channelId, int? channelNumber, string name)
    {
        if (channelId != null || channelNumber != null) return "";
        string n = name.ToLowerInvariant();
        if (SkyRe().IsMatch(n)) return "Sky";
        if (DaznRe().IsMatch(n)) return "DAZN";
        if (n.Contains("eurosport", StringComparison.Ordinal)) return "Eurosport";
        if (OtherRe().IsMatch(n)) return "Altri canali a pagamento";
        return "";
    }

    // abbonamenti (nomi di JustWatch/TMDB) che danno accesso ai canali di ogni piattaforma; la stessa tabella è in pay.ts
    static readonly Dictionary<string, Regex> Access = new()
    {
        ["Sky"] = new("^(sky|sky go|now|now tv)$", RegexOptions.IgnoreCase),
        ["DAZN"] = new("^dazn$", RegexOptions.IgnoreCase),
        ["Eurosport"] = new(@"^(eurosport|discovery\+|dazn|amazon prime video)$", RegexOptions.IgnoreCase),
    };

    /// <summary>La piattaforma è in chiaro ("") o tra gli abbonamenti. <paramref name="subs"/> null = profilo sconosciuto: si vede.</summary>
    public static bool Accessible(string platform, IReadOnlyList<string>? subs) =>
        platform.Length == 0 || subs == null || (Access.TryGetValue(platform, out var re) && subs.Any(s => re.IsMatch(s.Trim())));

    public static bool Visible(GuideProgramme p, IReadOnlyList<string>? subs) => Accessible(p.PayPlatform, subs);

    [GeneratedRegex(@"\bsky\b")] private static partial Regex SkyRe();
    [GeneratedRegex(@"\bdazn\b")] private static partial Regex DaznRe();
    [GeneratedRegex(@"\b(premium|mgm|amc|cine ?sony)\b")] private static partial Regex OtherRe();
}

/// <summary>
/// I passaggi in arrivo di un titolo per un profilo: il primo che vede (Visible), il primo che vede di sera (Evening, dalle
/// 20 alle 2) e il primo su un canale a pagamento che non ha (Paid). Un titolo può averli tutti: sulle locandine TV ed €
/// insieme (scelta di Antonio, 20/11/2026), salvo che lo veda già on demand o in casa.
/// </summary>
public sealed record WorkAirings(GuideProgramme? Visible, GuideProgramme? Evening, GuideProgramme? Paid)
{
    public static readonly WorkAirings None = new(null, null, null);

    /// <summary>Quanto avanti si guarda: tutta la guida (epgshare01 arriva a 4-5 giorni, una settimana al massimo).</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(9);

    public static WorkAirings Pick(IEnumerable<GuideProgramme> byStart, IReadOnlyList<string>? subs, TimeZoneInfo zone)
    {
        GuideProgramme? vis = null, eve = null, paid = null;
        foreach (var p in byStart)
        {
            if (PayChannels.Visible(p, subs))
            {
                vis ??= p;
                if (eve == null && TonightStore.IsEvening(p.Start, zone)) eve = p;
            }
            else paid ??= p;
            if (vis != null && eve != null && paid != null) break;
        }
        return new WorkAirings(vis, eve, paid);
    }

    /// <summary>Passaggi non ancora finiti delle opere indicate, per opera, in ordine di inizio (una sola lettura della guida).</summary>
    public static Dictionary<long, List<GuideProgramme>> Upcoming(GuideStore guide, IReadOnlyCollection<long> workIds, DateTimeOffset now)
    {
        if (workIds.Count == 0) return [];
        return guide.Query(new GuideQuery(now, now + Horizon) { WorkIds = workIds, Max = 0 })
            .Where(p => p.WorkId != null).GroupBy(p => p.WorkId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Start).ToList());
    }
}
