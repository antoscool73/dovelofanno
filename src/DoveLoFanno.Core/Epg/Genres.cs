using System.Text.RegularExpressions;

namespace DoveLoFanno.Epg;

/// <summary>
/// Classificazione delle categorie XMLTV nei gruppi fissi. Portata da RegistraTV.Genres senza cambiare le regole:
/// "Film" con episodio = Serie TV (Piedone su Sky); sottogeneri da film senza la parola "Film" contano come film
/// solo da 70 minuti in su (NCIS, The Mentalist restano Serie TV); "Film" sotto 20 minuti = Altro.
/// </summary>
public static partial class Genres
{
    public const string Film = "Film";
    public const string Series = "Serie TV";
    public const string Documentaries = "Documentari";
    public const string News = "Notizie";
    public const string Sport = "Sport";
    public const string Entertainment = "Intrattenimento";
    public const string Kids = "Bambini";
    public const string Other = "Altro";

    public static readonly IReadOnlyList<string> Groups = [Film, Series, Documentaries, News, Sport, Entertainment, Kids, Other];

    static readonly HashSet<string> KidsCats = ["cartoni animati", "cartoni", "bambini", "per i bimbi piccoli", "anime", "ragazzi", "kids"];
    static readonly HashSet<string> SeriesCats = ["serie tv", "serie", "fiction", "telefilm", "soap", "soap opera", "sitcom", "miniserie", "telenovela"];
    static readonly HashSet<string> SportCats = ["sport", "calcio", "tennis", "motori", "basket", "pallavolo", "volley", "equestri", "ciclismo", "rugby", "golf", "atletica", "nuoto", "sci", "sport invernali", "boxe", "wrestling", "football americano", "baseball", "pallamano", "sport estremi", "formula 1", "motociclismo"];
    static readonly HashSet<string> NewsCats = ["notiziario", "news", "telegiornale", "tg", "informazione", "economia", "attualita", "politica", "meteo", "approfondimento", "inchiesta", "rubrica"];
    static readonly HashSet<string> DocsCats = ["documentario", "documentari", "natura", "storia", "arte e cultura", "scoperta e avventura", "scienza", "scienze", "viaggi", "societa", "cultura", "animali", "tecnologia", "mondo e tendenze"];
    static readonly HashSet<string> FunCats = ["intrattenimento", "show", "talk show", "gioco", "quiz", "reality", "reality show", "varieta", "musica", "cucina", "magazine", "caccia", "pesca", "moda", "lifestyle", "game show", "concerto", "teatro", "ragazzi e musica"];
    // categorie generiche che non dicono il sottogenere di un film
    static readonly HashSet<string> NoSub = ["film", "cinema", "altri", "altro", "altri programmi"];
    // sottogeneri da film senza la parola "Film" (alcune fonti scrivono solo "Commedia")
    static readonly HashSet<string> FilmSubs = ["commedia", "drammatico", "azione", "thriller", "horror", "fantascienza", "western", "poliziesco", "giallo", "sentimentale", "avventura", "fantastico", "guerra", "biografico", "storico", "musicale", "noir", "animazione", "comico", "erotico", "grottesco", "spionaggio", "catastrofico", "mistero"];

    static bool Any(List<string> cats, HashSet<string> set) => cats.Exists(set.Contains);

    /// <summary>Imposta Group, Sub e GroupGuessed. <paramref name="hint"/>: id e nomi del canale nella guida.</summary>
    public static void Classify(EpgProgramme p, string hint)
    {
        var cats = p.Categories.Select(Titles.Plain).ToList();
        string? g = null;
        string sub = "";
        bool hasEp = p.Episode.Length > 0;
        double mins = (p.Stop - p.Start).TotalMinutes;
        if (cats.Contains("film"))
        {
            if (hasEp) g = Series;
            else if (mins < 20) g = Other;
            else
            {
                g = Film;
                for (int i = 0; i < cats.Count; i++)
                    if (!NoSub.Contains(cats[i])) { sub = Cap(p.Categories[i]); break; }
            }
        }
        else if (cats.Contains("informazione") && cats.Contains("sport")) g = Sport;
        else if (Any(cats, KidsCats)) g = Kids;
        else if (Any(cats, SeriesCats)) g = Series;
        else if (Any(cats, SportCats)) g = Sport;
        else if (cats.Contains("documentario") || cats.Contains("documentari")) g = Documentaries;
        else if (Any(cats, NewsCats)) g = News;
        else if (cats.Contains("talk show")) g = Entertainment;
        else if (Any(cats, DocsCats) && !cats.Contains("cucina") && !cats.Contains("caccia") && !cats.Contains("pesca") && !cats.Contains("musica")) g = Documentaries;
        else if (Any(cats, FunCats)) g = Entertainment;
        else if (cats.Count > 0)
        {
            if (Any(cats, FilmSubs) && !hasEp && mins >= 70) { g = Film; sub = Cap(p.Categories[0]); }
            else if (Any(cats, FilmSubs)) g = Series;
        }
        p.GroupGuessed = false;
        if (g == null)
        {
            g = Guess(p, hint);
            p.GroupGuessed = true;
        }
        p.Group = g;
        p.Sub = sub;
    }

    static string Cap(string? s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return s;
        return char.ToUpper(s[0]) + s[1..];
    }

    // programmi senza genere: dal titolo e dal canale
    static string Guess(EpgProgramme p, string hint)
    {
        string t = Titles.Plain(p.Title);
        if (NewsTitleStart().IsMatch(t) || NewsTitleWord().IsMatch(t)) return News;
        string h = Titles.Plain(hint);
        double mins = (p.Stop - p.Start).TotalMinutes;
        if (MovieChannel().IsMatch(h)) return p.Episode.Length > 0 ? Series : mins >= 60 ? Film : Other;
        if (NewsChannel().IsMatch(h)) return News;
        if (SportChannel().IsMatch(h)) return Sport;
        if (KidsChannel().IsMatch(h)) return Kids;
        if (DocsChannel().IsMatch(h)) return Documentaries;
        if (FictionChannel().IsMatch(h)) return p.Episode.Length > 0 || p.SubTitle.Length > 0 ? Series : Other;
        if (p.Episode.Length > 0) return Series;
        return Other;
    }

    [GeneratedRegex(@"^(tg\s?\d*|tg[a-z0-9]+|telegiornale|notiziario|meteo|rassegna stampa|news\b|edicola)")]
    private static partial Regex NewsTitleStart();

    [GeneratedRegex(@"\b(tg[0-9]?|telegiornale|giornale radio|rainews|rainews24|tgcom24|gr\s?[0-9])\b")]
    private static partial Regex NewsTitleWord();

    [GeneratedRegex("movie|cinema|cine|iris|film")]
    private static partial Regex MovieChannel();

    [GeneratedRegex("news|tg24|tgcom|tgnorba|euronews|rainews|parlamento")]
    private static partial Regex NewsChannel();

    [GeneratedRegex("sport|eurosport|supertennis|motor|calcio|bike")]
    private static partial Regex SportChannel();

    [GeneratedRegex(@"kids|boing|cartoon|frisbee|\bk2\b|yoyo|gulp|nick|super!|deakids|cartoonito|boomerang")]
    private static partial Regex KidsChannel();

    [GeneratedRegex("storia|focus|doc|nature|natura|geo|history|scienz")]
    private static partial Regex DocsChannel();

    [GeneratedRegex("crime|giallo|topcrime|fiction|serie|premium")]
    private static partial Regex FictionChannel();
}
