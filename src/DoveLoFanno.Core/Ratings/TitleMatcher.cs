using System.Text.RegularExpressions;
using DoveLoFanno.Epg;

namespace DoveLoFanno.Ratings;

/// <summary>Esito della ricerca di un film su TMDB.</summary>
public sealed record MatchResult(TmdbMovie? Movie, string ImdbId)
{
    public static readonly MatchResult NotFound = new(null, "");
    public bool Found => Movie != null;
}

/// <summary>
/// Abbinamento tra titolo della guida e film di TMDB, portato da RatingService di RegistraTV (regole versione 2,
/// 14/11/2026). Parole del titolo senza articoli e congiunzioni e senza ordine; titolo uguale vale più di titolo
/// contenuto; anno giusto ±1 = bonus forte; un film noto (≥ 20 voti) batte un omonimo sconosciuto.
/// </summary>
public static partial class TitleMatcher
{
    /// <summary>Versione delle regole: le voci senza voto cercate con regole più vecchie si ricercano una volta.</summary>
    public const int MatchVersion = 2;

    static readonly HashSet<string> StopWords = ["e", "and", "il", "lo", "la", "i", "gli", "le", "l", "un", "uno", "una", "the", "a", "an", "of", "di", "da", "del", "della", "dei", "de", "in"];

    /// <summary>Parole del titolo senza articoli, congiunzioni e preposizioni brevi, senza doppioni, in ordine alfabetico.</summary>
    public static List<string> Words(string? s)
    {
        var w = new List<string>();
        foreach (string x in Titles.TitleKey(s).Split(' '))
            if (x.Length > 0 && !StopWords.Contains(x) && !w.Contains(x)) w.Add(x);
        w.Sort(StringComparer.Ordinal);
        return w;
    }

    /// <summary>
    /// 2 = stesso titolo, 1 = uno contiene l'altro (titoli Mediaset che uniscono originale e italiano,
    /// "Agente 007 - Zona pericolo" / "007 - Zona pericolo"), 0 = diverso.
    /// </summary>
    public static int Match(List<string> a, List<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        bool aInB = a.TrueForAll(b.Contains);
        bool bInA = b.TrueForAll(a.Contains);
        if (aInB && bInA) return 2;
        int small = Math.Min(a.Count, b.Count);
        if ((aInB || bInA) && small >= 2 && Math.Abs(a.Count - b.Count) <= 3) return 1;
        return 0;
    }

    static bool AllIn(List<string> a, List<string> b) => a.Count > 0 && a.TrueForAll(b.Contains);

    /// <summary>2 = titolo (italiano o originale) uguale, 1 = contenuto, 0 = diverso.</summary>
    public static int TitleMatch(TmdbMovie m, List<string> tw) =>
        Math.Max(Match(tw, Words(m.Title)), Match(tw, Words(m.OriginalTitle)));

    /// <summary>Il risultato migliore per le parole del titolo e l'anno della guida (0 = sconosciuto), o null.</summary>
    public static TmdbMovie? Pick(IEnumerable<TmdbMovie> results, List<string> tw, int year)
    {
        TmdbMovie? best = null;
        double bestScore = double.MinValue;
        foreach (var m in results)
        {
            int k = TitleMatch(m, tw);
            int ry = m.Year;
            bool yearOk = year > 0 && ry > 0 && Math.Abs(ry - year) <= 1;
            bool known = m.VoteCount >= 20;
            // titolo di una parola contenuto in quello di un film noto dell'anno giusto: "Chase" (2022) = "Chase - Scomparsa" (2022)
            if (k == 0 && yearOk && known && (AllIn(tw, Words(m.Title)) || AllIn(tw, Words(m.OriginalTitle)))) k = 1;
            if (k == 0) continue;
            // titolo solo contenuto: serve l'anno giusto, oppure la guida non ha l'anno (Mediaset)
            if (k == 1 && !yearOk && year > 0) continue;
            double score = (k == 2 ? 20 : 10) + (yearOk ? 30 : 0) + (year > 0 && ry == year ? 2 : 0) + (known ? 15 : 0);
            score = score * 100000 + Math.Min(99999, m.VoteCount);
            if (score > bestScore) { bestScore = score; best = m; }
        }
        return best;
    }

    /// <summary>"Le sorelle Macaluso (film)" diventa "Le sorelle Macaluso".</summary>
    public static string QueryTitle(string title) => FilmSuffix().Replace(title, "").Trim();

    /// <summary>Ricerca completa di un film della guida, con le stesse chiamate a TMDB di RegistraTV.</summary>
    public static async Task<MatchResult> ResolveAsync(ITmdbSearch tmdb, string title, int year, CancellationToken ct = default)
    {
        string q = QueryTitle(title);
        var tw = Words(q);
        var best = Pick(await tmdb.SearchMovieAsync(q, year, ct), tw, year);
        // l'anno della guida a volte è sbagliato (Rai Movie mette spesso 2019): si riprova senza
        if (best == null && year > 0) best = Pick(await tmdb.SearchMovieAsync(q, 0, ct), tw, year);
        // con l'anno sbagliato si trova a volte un omonimo sconosciuto di quell'anno (Phenomenon "2019" = un film
        // con 0 voti, quello trasmesso è del 1996): un film con lo stesso titolo e molti voti è quasi sempre quello giusto
        else if (best != null && year > 0 && best.VoteCount < 20)
        {
            var famous = Pick(await tmdb.SearchMovieAsync(q, 0, ct), tw, 0);
            if (famous != null && famous.VoteCount >= 100 && TitleMatch(famous, tw) == 2) best = famous;
        }
        if (best == null) return MatchResult.NotFound;
        string imdb;
        try { imdb = await tmdb.ImdbIdAsync(best.Id, ct); }
        catch (HttpRequestException) { imdb = ""; }
        return new MatchResult(best, imdb);
    }

    [GeneratedRegex(@"\s*\((film|tv|\d{4})\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FilmSuffix();
}
