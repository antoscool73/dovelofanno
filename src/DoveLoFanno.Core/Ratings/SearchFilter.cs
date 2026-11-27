using System.Globalization;
using DoveLoFanno.Epg;

namespace DoveLoFanno.Ratings;

/// <summary>
/// Filtri della ricerca (tutti facoltativi): tipo (all, film, series, documentary), generi (codici TMDB dei film: vale uno
/// qualsiasi; nessuno = tutti), anni, paese di produzione (ISO, "" = tutti), voto minimo, disponibilità (tv, mine, paid,
/// home: vale uno qualsiasi di quelli scelti; nessuno = ovunque, anche non disponibile). HideSeen e HideHome tolgono i
/// titoli già visti dal profilo e quelli già in casa (19/11/2026, liste dinamiche). Services = servizi di streaming
/// (nomi delle piattaforme, uno qualsiasi; nessuno = tutti): il titolo dev'essere nel catalogo di uno di loro (27/11/2026,
/// link dalle colonne di «Il top per la serata»).
/// </summary>
public sealed record SearchFilter(string Kind = "all", IReadOnlyList<int>? Genres = null, int YearFrom = 0, int YearTo = 0, string Country = "",
    double MinRating = 0, IReadOnlySet<string>? Avail = null, bool HideSeen = false, bool HideHome = false, IReadOnlyList<string>? Services = null)
{
    public IReadOnlyList<string> ServiceList => Services ?? [];
    public IReadOnlySet<string> AvailSet => Avail ?? new HashSet<string>();
    public IReadOnlyList<int> GenreList => Genres ?? [];
    public bool Any => Kind != "all" || GenreList.Count > 0 || YearFrom > 0 || YearTo > 0 || Country.Length > 0 || MinRating > 0 || AvailSet.Count > 0
        || HideSeen || HideHome || ServiceList.Count > 0;

    /// <summary>In streaming su uno dei servizi scelti ("Netflix basic with Ads" vale come "Netflix"); nessuno scelto = sì.</summary>
    public bool ServicesOk(IReadOnlyList<string> streaming) =>
        ServiceList.Count == 0 || streaming.Any(s => ServiceList.Any(m =>
            s.Equals(m, StringComparison.OrdinalIgnoreCase) || s.StartsWith(m + " ", StringComparison.OrdinalIgnoreCase)));

    public bool KindOk(string kind) => Kind switch
    {
        "film" => kind == "film",
        "series" => kind == "series",
        "documentary" => kind == "documentary",
        _ => true,
    };

    public bool YearOk(int year) =>
        (YearFrom <= 0 || (year > 0 && year >= YearFrom)) && (YearTo <= 0 || (year > 0 && year <= YearTo));
}

/// <summary>Generi TMDB con il nome italiano; per le serie i codici equivalenti (Azione = Action &amp; Adventure...).</summary>
public static class TmdbGenres
{
    public sealed record Genre(int Id, string Name, int[] Tv);

    public static readonly Genre[] All =
    [
        new(28, "Azione", [10759]), new(12, "Avventura", [10759]), new(16, "Animazione", [16]), new(35, "Commedia", [35]),
        new(80, "Crime", [80]), new(99, "Documentario", [99]), new(18, "Drammatico", [18]), new(10751, "Famiglia", [10751, 10762]),
        new(14, "Fantasy", [10765]), new(36, "Storia", []), new(27, "Horror", []), new(10402, "Musica", []),
        new(9648, "Mistero", [9648]), new(10749, "Romantico", []), new(878, "Fantascienza", [10765]), new(53, "Thriller", []),
        new(10752, "Guerra", [10768]), new(37, "Western", [37]),
    ];

    static readonly Dictionary<int, string> Names = new()
    {
        [28] = "Azione", [12] = "Avventura", [16] = "Animazione", [35] = "Commedia", [80] = "Crime", [99] = "Documentario",
        [18] = "Drammatico", [10751] = "Famiglia", [14] = "Fantasy", [36] = "Storia", [27] = "Horror", [10402] = "Musica",
        [9648] = "Mistero", [10749] = "Romantico", [878] = "Fantascienza", [10770] = "Film TV", [53] = "Thriller",
        [10752] = "Guerra", [37] = "Western", [10759] = "Azione e avventura", [10762] = "Bambini", [10763] = "Notizie",
        [10764] = "Reality", [10765] = "Fantascienza e fantasy", [10766] = "Soap", [10767] = "Talk show", [10768] = "Guerra e politica",
    };

    public static string Name(int id) => Names.TryGetValue(id, out var n) ? n : "";

    /// <summary>Codici che valgono per il genere scelto (film e serie).</summary>
    public static HashSet<int> Ids(int genre)
    {
        var g = All.FirstOrDefault(x => x.Id == genre);
        return g == null ? [genre] : [g.Id, .. g.Tv];
    }

    /// <summary>Un'opera è di uno dei generi (nessuno = tutti): dai codici TMDB, oppure (senza codici, solo guida) dal genere scritto dalla guida.</summary>
    public static bool Matches(IReadOnlyList<int> genres, IReadOnlyCollection<int> ids, string guideText) =>
        genres.Count == 0 || genres.Any(g => Matches(g, ids, guideText));

    /// <summary>Un'opera è del genere: dai codici TMDB, oppure (senza codici, solo guida) dal genere scritto dalla guida.</summary>
    public static bool Matches(int genre, IReadOnlyCollection<int> ids, string guideText)
    {
        if (genre <= 0) return true;
        if (ids.Count > 0) return ids.Overlaps(Ids(genre));
        var g = All.FirstOrDefault(x => x.Id == genre);
        if (g == null || guideText.Length == 0) return false;
        string t = Titles.TitleKey(guideText), n = Titles.TitleKey(g.Name);
        // "Drammatico" nella guida è anche "Dramma"; "Romantico" anche "Sentimentale"
        return t.Contains(n.Length > 6 ? n[..6] : n, StringComparison.Ordinal)
            || (genre == 10749 && t.Contains("sentimental", StringComparison.Ordinal));
    }

    /// <summary>
    /// Il genere TMDB più vicino a un sottogenere della guida ("Poliziesco" = Crime, "Giallo" = Mistero), 0 se non c'è:
    /// serve a convertire le preferenze di prima (generi della guida) in liste dinamiche.
    /// </summary>
    public static int FromGuide(string sub)
    {
        string k = Titles.TitleKey(sub);
        if (k.Length == 0) return 0;
        foreach (var (word, id) in GuideWords)
            if (k.Contains(word, StringComparison.Ordinal)) return id;
        return All.FirstOrDefault(g => Titles.TitleKey(g.Name) == k)?.Id ?? 0;
    }

    static readonly (string Word, int Id)[] GuideWords =
    [
        ("thriller", 53), ("poliziesc", 80), ("crime", 80), ("noir", 80), ("giallo", 9648), ("mister", 9648), ("comic", 35), ("commedia", 35),
        ("dramm", 18), ("dramma", 18), ("azione", 28), ("avventur", 12), ("animazion", 16), ("cartoni", 16), ("horror", 27), ("fantascienz", 878),
        ("fantasy", 14), ("fantastic", 14), ("guerra", 10752), ("western", 37), ("romantic", 10749), ("sentimental", 10749), ("documentar", 99),
        ("music", 10402), ("famiglia", 10751), ("storic", 36), ("biografic", 36),
    ];

    static bool Overlaps(this IReadOnlyCollection<int> a, HashSet<int> b) => a.Any(b.Contains);
}

/// <summary>Paesi di produzione più comuni, con il nome italiano usato dalla guida ("Italia, USA").</summary>
public static class Countries
{
    public static readonly (string Code, string Name)[] All =
    [
        ("IT", "Italia"), ("US", "USA"), ("GB", "Regno Unito"), ("FR", "Francia"), ("DE", "Germania"), ("ES", "Spagna"),
        ("JP", "Giappone"), ("KR", "Corea del Sud"), ("CA", "Canada"), ("AU", "Australia"), ("IN", "India"), ("SE", "Svezia"),
        ("DK", "Danimarca"), ("BE", "Belgio"), ("IE", "Irlanda"), ("MX", "Messico"), ("BR", "Brasile"), ("CN", "Cina"),
    ];

    public static string Name(string code) =>
        All.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase)).Name ?? code;

    /// <summary>Il testo della guida ("Italia, USA") contiene il paese: anche "Stati Uniti" per gli USA.</summary>
    public static bool InGuideText(string code, string text)
    {
        if (code.Length == 0) return true;
        if (text.Length == 0) return false;
        string t = text.ToLower(CultureInfo.InvariantCulture);
        string n = Name(code).ToLower(CultureInfo.InvariantCulture);
        return t.Contains(n, StringComparison.Ordinal) || (code == "US" && (t.Contains("stati uniti") || t.Contains("usa")));
    }
}
