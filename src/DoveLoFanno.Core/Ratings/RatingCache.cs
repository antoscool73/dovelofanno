using System.Globalization;
using System.Text;
using DoveLoFanno.Epg;

namespace DoveLoFanno.Ratings;

/// <summary>Voce della cache: un film cercato (trovato o no) con i voti TMDB e IMDb.</summary>
public sealed class RatingEntry
{
    public bool Found { get; set; }
    public int TmdbId { get; set; }
    public string ImdbId { get; set; } = "";
    public string OriginalTitle { get; set; } = "";
    public int Year { get; set; }
    public double TmdbVote { get; set; }
    public int TmdbCount { get; set; }
    public double ImdbVote { get; set; }
    public int ImdbVotes { get; set; }
    public DateTime Checked { get; set; }
    public DateTime ImdbChecked { get; set; }
    public int Ver { get; set; } = TitleMatcher.MatchVersion;

    /// <summary>Senza voto mostrabile: né IMDb né almeno 20 voti TMDB.</summary>
    public bool NoUsableVote => ImdbVote <= 0 && TmdbCount < 20;

    public static RatingEntry From(MatchResult r, DateTime now)
    {
        var e = new RatingEntry { Checked = now };
        if (r.Movie is { } m)
        {
            e.Found = true;
            e.TmdbId = m.Id;
            e.OriginalTitle = m.OriginalTitle;
            e.Year = m.Year;
            e.TmdbVote = m.VoteAverage;
            e.TmdbCount = m.VoteCount;
            e.ImdbId = r.ImdbId;
        }
        return e;
    }
}

/// <summary>Voto mostrato per un film: IMDb se c'è, altrimenti TMDB con almeno 20 voti.</summary>
public sealed record RatingInfo(double Value, string Source, int Votes, string ImdbId, int TmdbId, string OriginalTitle, int Year);

/// <summary>
/// Cache dei voti, nello stesso formato di ratings\cache.tsv di RegistraTV (13 colonne separate da tabulazioni,
/// la tredicesima è la versione delle regole; senza = versione 1), così si può importare quella di oggi.
/// Non è thread-safe: la protegge <see cref="RatingService"/>.
/// </summary>
public sealed class RatingCache
{
    const string TimeFormat = "yyyyMMddHHmm";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public Dictionary<string, RatingEntry> Entries { get; } = [];

    /// <summary>Chiave della cache: titolo normalizzato e anno della guida ("padrino|1972").</summary>
    public static string KeyOf(string title, int year) => Titles.TitleKey(title) + "|" + year.ToString(Inv);

    /// <param name="useImdb">false = solo voti TMDB (il file IMDb è facoltativo: licenza solo personale)</param>
    public RatingInfo? Lookup(string title, int year, bool useImdb = true)
    {
        if (!Entries.TryGetValue(KeyOf(title, year), out var e) || !e.Found) return null;
        if (useImdb && e.ImdbVote > 0) return new(e.ImdbVote, "IMDb", e.ImdbVotes, e.ImdbId, e.TmdbId, e.OriginalTitle, e.Year);
        if (e.TmdbVote > 0 && e.TmdbCount >= 20) return new(e.TmdbVote, "TMDB", e.TmdbCount, e.ImdbId, e.TmdbId, e.OriginalTitle, e.Year);
        return null;
    }

    /// <summary>
    /// Da cercare: mai cercato; non trovato da 30 giorni o più; oppure senza voto e cercato con regole più vecchie.
    /// </summary>
    public bool NeedsSearch(string key, DateTime now)
    {
        if (key.StartsWith('|')) return false;
        if (!Entries.TryGetValue(key, out var e)) return true;
        bool fresh = e.Found || (now - e.Checked).TotalDays < 30;
        return !fresh || (e.NoUsableVote && e.Ver < TitleMatcher.MatchVersion);
    }

    public static RatingCache Load(string path)
    {
        var c = new RatingCache();
        if (!File.Exists(path)) return c;
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            if (TryParse(line, out var key, out var e)) c.Entries[key] = e;
        return c;
    }

    internal static bool TryParse(string line, out string key, out RatingEntry e)
    {
        key = "";
        e = new RatingEntry();
        string[] f = line.Split('\t');
        if (f.Length < 12) return false;
        try
        {
            e.Found = f[1] == "1";
            e.TmdbId = int.Parse(f[2], Inv);
            e.ImdbId = f[3];
            e.OriginalTitle = f[4];
            e.Year = int.Parse(f[5], Inv);
            e.TmdbVote = double.Parse(f[6], Inv);
            e.TmdbCount = int.Parse(f[7], Inv);
            e.ImdbVote = double.Parse(f[8], Inv);
            e.ImdbVotes = int.Parse(f[9], Inv);
            e.Checked = DateTime.ParseExact(f[10], TimeFormat, Inv);
            e.ImdbChecked = DateTime.ParseExact(f[11], TimeFormat, Inv);
            e.Ver = f.Length > 12 ? int.Parse(f[12], Inv) : 1;
        }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
        key = f[0];
        return true;
    }

    internal static string Format(string key, RatingEntry e) => string.Join('\t',
        key, e.Found ? "1" : "0", e.TmdbId.ToString(Inv), e.ImdbId, e.OriginalTitle.Replace('\t', ' '),
        e.Year.ToString(Inv), e.TmdbVote.ToString(Inv), e.TmdbCount.ToString(Inv),
        e.ImdbVote.ToString(Inv), e.ImdbVotes.ToString(Inv),
        e.Checked.ToString(TimeFormat, Inv), e.ImdbChecked.ToString(TimeFormat, Inv), e.Ver.ToString(Inv));

    /// <summary>Salva sostituendo il file solo a scrittura finita; le voci non trovate da più di 60 giorni si tolgono.</summary>
    public void Save(string path, DateTime now)
    {
        var lines = Entries
            .Where(kv => kv.Value.Found || (now - kv.Value.Checked).TotalDays <= 60)
            .Select(kv => Format(kv.Key, kv.Value));
        string tmp = path + ".tmp";
        File.WriteAllLines(tmp, lines, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}
