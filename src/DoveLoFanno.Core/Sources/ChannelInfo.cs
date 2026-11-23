using System.Text.Json;
using System.Text.RegularExpressions;
using DoveLoFanno.Epg;

namespace DoveLoFanno.Sources;

/// <summary>Regione, tipo e stato di un canale, per il filtro «Canali» della guida.</summary>
/// <param name="Region">regione italiana ricavata dal nome («Bergamo Tv» → Lombardia), "" se non si capisce</param>
/// <param name="Kind">radio, news, sports, kids, music, religious, shop, movies, culture, entertainment, general o ""</param>
/// <param name="Closed">il canale ha chiuso secondo il database iptv-org</param>
public sealed record ChannelFacts(string Region, string Kind, bool Closed);

/// <summary>
/// Filtro intelligente dei canali della guida (23/11/2026, richiesta di Antonio: con una playlist di canali locali la guida
/// aveva centinaia di righe). Due fonti:
/// - la regione si ricava dal nome del canale, con le regioni, i capoluoghi di provincia e qualche città e rete locale
///   nota: nessun database pubblico la dà (iptv-org la riporta quasi mai, le playlist mettono tutto in «Italy»);
/// - il tipo (notizie, sport, bambini, televendite, religioso...) e se ha chiuso vengono da una copia dei canali italiani
///   del database iptv-org (github.com/iptv-org/database, pubblico dominio), presa il 23/11/2026 (iptv-org-it.json).
///   Le radio si riconoscono dal gruppo della playlist o dal nome.
/// </summary>
public static partial class ChannelInfo
{
    // regione → nomi che la indicano nel nome di un canale (regione, aggettivi, capoluoghi, città, reti note)
    static readonly (string Region, string[] Words)[] Regions =
    [
        ("Abruzzo", ["abruzzo", "abruzzese", "aquila", "chieti", "pescara", "teramo"]),
        ("Basilicata", ["basilicata", "lucania", "lucana", "potenza", "matera"]),
        ("Calabria", ["calabria", "calabra", "catanzaro", "cosenza", "crotone", "reggio calabria", "vibo", "castrovillari"]),
        ("Campania", ["campania", "campana", "napoli", "avellino", "benevento", "caserta", "salerno", "irpinia"]),
        ("Emilia-Romagna", ["emilia", "romagna", "bologna", "ferrara", "forli", "cesena", "modena", "parma", "piacenza", "ravenna", "reggio emilia", "rimini", "telereggio"]),
        ("Friuli-Venezia Giulia", ["friuli", "trieste", "udine", "pordenone", "gorizia"]),
        ("Lazio", ["lazio", "roma", "frosinone", "latina", "rieti", "viterbo", "ciociaria"]),
        ("Liguria", ["liguria", "ligure", "genova", "imperia", "la spezia", "savona", "sanremo", "primocanale"]),
        ("Lombardia", ["lombardia", "lombarda", "milano", "bergamo", "brescia", "como", "cremona", "lecco", "lodi", "mantova", "monza", "pavia", "sondrio", "varese", "telelombardia"]),
        ("Marche", ["marche", "ancona", "ascoli", "macerata", "pesaro", "urbino"]),
        ("Molise", ["molise", "campobasso", "isernia"]),
        ("Piemonte", ["piemonte", "torino", "alessandria", "asti", "biella", "cuneo", "novara", "verbania", "vco", "vercelli"]),
        ("Puglia", ["puglia", "pugliese", "salento", "bari", "barletta", "andria", "trani", "brindisi", "foggia", "lecce", "taranto", "telenorba", "norba", "antenna sud"]),
        ("Sardegna", ["sardegna", "sarda", "cagliari", "nuoro", "oristano", "sassari", "olbia", "gallura", "videolina", "aristanis"]),
        ("Sicilia", ["sicilia", "siciliana", "palermo", "agrigento", "caltanissetta", "catania", "enna", "messina", "ragusa", "siracusa", "trapani"]),
        ("Toscana", ["toscana", "firenze", "arezzo", "grosseto", "livorno", "lucca", "massa", "carrara", "pisa", "pistoia", "prato", "siena", "versilia"]),
        ("Trentino-Alto Adige", ["trentino", "trento", "bolzano", "alto adige", "sudtirol", "suedtirol"]),
        ("Umbria", ["umbria", "perugia", "terni"]),
        ("Valle d'Aosta", ["valle d aosta", "aosta"]),
        ("Veneto", ["veneto", "veneta", "venezia", "belluno", "padova", "rovigo", "treviso", "verona", "vicenza", "telenuovo"]),
    ];

    /// <summary>Le venti regioni, nell'ordine dell'elenco.</summary>
    public static IReadOnlyList<string> RegionNames { get; } = [.. Regions.Select(r => r.Region).Order(StringComparer.Ordinal)];

    static readonly (Regex Re, string Region)[] RegionRes =
        [.. Regions.SelectMany(r => r.Words.Select(w => (new Regex(@"(?<![a-z0-9])" + Regex.Escape(w).Replace(@"\ ", @"\s+") + @"(?![a-z0-9])", RegexOptions.CultureInvariant), r.Region)))];

    /// <summary>Regione dal nome del canale, "" se il nome non la dice.</summary>
    public static string RegionOf(string name)
    {
        string n = Plain(name);
        foreach (var (re, region) in RegionRes)
            if (re.IsMatch(n)) return region;
        return "";
    }

    // minuscole, senza accenti e con la punteggiatura come spazio: «Forlì-Cesena» → «forli cesena»
    static string Plain(string s)
    {
        string d = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (char ch in d)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return NonAlnum().Replace(sb.ToString(), " ").Trim();
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();

    sealed record IptvOrgChannel(string Id, string Name, string[] Alt, string[] Cat, bool Closed);

    // chiave normalizzata (come per la guida, Titles.ChannelKey, senza «tv» finale) → canale del database
    static readonly Lazy<Dictionary<string, IptvOrgChannel>> Db = new(() =>
    {
        var asm = typeof(ChannelInfo).Assembly;
        string res = asm.GetManifestResourceNames().Single(n => n.EndsWith("iptv-org-it.json", StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(res)!;
        var list = JsonSerializer.Deserialize<List<IptvOrgChannel>>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        var d = new Dictionary<string, IptvOrgChannel>(StringComparer.Ordinal);
        foreach (var c in list)
            foreach (string n in c.Alt.Prepend(c.Name).Prepend(c.Id))
                if (Key(n) is { Length: > 0 } k) d.TryAdd(k, c);
        return d;
    });

    static string Key(string s)
    {
        string k = Titles.ChannelKey(s);
        return k.EndsWith("tv", StringComparison.Ordinal) && k.Length > 4 ? k[..^2] : k;
    }

    // categorie di iptv-org → il nostro tipo (la prima che si conosce)
    static readonly Dictionary<string, string> Kinds = new(StringComparer.Ordinal)
    {
        ["news"] = "news", ["business"] = "news", ["weather"] = "news", ["legislative"] = "news",
        ["sports"] = "sports", ["outdoor"] = "sports", ["auto"] = "sports",
        ["kids"] = "kids", ["animation"] = "kids", ["family"] = "kids",
        ["music"] = "music", ["religious"] = "religious", ["shop"] = "shop",
        ["movies"] = "movies", ["series"] = "movies", ["classic"] = "movies",
        ["culture"] = "culture", ["education"] = "culture", ["documentary"] = "culture", ["science"] = "culture",
        ["entertainment"] = "entertainment", ["lifestyle"] = "entertainment", ["travel"] = "entertainment", ["cooking"] = "entertainment",
        ["comedy"] = "entertainment", ["general"] = "general",
    };

    /// <summary>Regione, tipo e chiusura di un canale della playlist.</summary>
    public static ChannelFacts For(string key, string name, string group)
    {
        string id = key.Contains(':') ? key[(key.IndexOf(':') + 1)..] : key;
        IptvOrgChannel? c = null;
        foreach (string k in new[] { Key(id), Key(name) })
            if (k.Length > 0 && Db.Value.TryGetValue(k, out c)) break;
        string kind = group.Equals("radio", StringComparison.OrdinalIgnoreCase) || RadioWord().IsMatch(Plain(name)) ? "radio"
            : c?.Cat.Select(x => Kinds.GetValueOrDefault(x)).FirstOrDefault(x => x != null) ?? "";
        return new ChannelFacts(RegionOf(name), kind, c?.Closed ?? false);
    }

    [GeneratedRegex(@"(?<![a-z0-9])radio(?![a-z0-9])")]
    private static partial Regex RadioWord();
}
