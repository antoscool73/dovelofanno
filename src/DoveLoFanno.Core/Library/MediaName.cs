using System.Globalization;
using System.Text.RegularExpressions;

namespace DoveLoFanno.Library;

/// <summary>Quello che si capisce dal nome di un file: titolo, anno e, per gli episodi, serie, stagione ed episodio.</summary>
public sealed record NameGuess(string Title, int Year, int? Season, int? Episode, string EpisodeTitle)
{
    public bool IsEpisode => Season is not null && Episode is not null;
}

/// <summary>
/// Riconoscimento dei file della libreria dal nome, come fanno Jellyfin e Kodi: "Titolo (2010).mkv",
/// "Titolo.2010.1080p.BluRay.x264-GRUPPO.mkv", "Serie/Stagione 01/Serie - S01E02 - Titolo.mp4", "Serie 1x02.avi".
/// </summary>
public static partial class MediaName
{
    public static readonly IReadOnlySet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".ts", ".m2ts", ".mts", ".mpg", ".mpeg", ".webm", ".wmv", ".flv", ".divx", ".ogv",
    };

    /// <summary>Cartelle che non contengono opere: extra dei dischi, cestini e indici dei NAS.</summary>
    static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "extras", "extra", "featurettes", "trailers", "trailer", "behind the scenes", "deleted scenes", "interviews", "scenes",
        "shorts", "samples", "sample", "@eadir", "#recycle", "$recycle.bin", "#snapshot", ".trash", "lost+found", "system volume information",
    };

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    /// <summary>true per le cartelle da saltare (nascoste, extra, cestini).</summary>
    public static bool SkipFolder(string name) => name.StartsWith('.') || SkipFolders.Contains(name);

    /// <summary>true per i file da saltare: nascosti e campioni ("sample").</summary>
    public static bool SkipFile(string name) => name.StartsWith('.') || SampleWord().IsMatch(Path.GetFileNameWithoutExtension(name));

    /// <summary>Riconosce il file: <paramref name="path"/> completo, per guardare anche le cartelle che lo contengono.</summary>
    public static NameGuess Parse(string path)
    {
        string file = Path.GetFileNameWithoutExtension(path);
        string? dir = Path.GetDirectoryName(path);
        string parent = dir == null ? "" : Path.GetFileName(dir);
        string grand = dir == null ? "" : Path.GetFileName(Path.GetDirectoryName(dir) ?? "");

        // episodi: S01E02, 1x02, oppure "Episodio 2" dentro una cartella "Stagione 1"
        var m = SeasonEpisode().Match(file);
        if (!m.Success) m = CrossEpisode().Match(file);
        if (m.Success)
        {
            int season = Int(m.Groups["s"].Value), episode = Int(m.Groups["e"].Value);
            string show = Clean(file[..m.Index]).Title;
            string rest = Clean(file[(m.Index + m.Length)..], rest: true).Title;
            if (show.Length == 0) show = SeriesFolder(parent, grand);
            var sy = Clean(show);
            return new NameGuess(sy.Title, sy.Year, season, episode, rest);
        }
        var sf = SeasonFolder().Match(parent);
        var ef = EpisodeOnly().Match(file);
        if (sf.Success && ef.Success)
        {
            string show = Clean(file[..ef.Index]).Title;
            if (show.Length == 0) show = Clean(grand).Title;
            var sy = Clean(show);
            return new NameGuess(sy.Title, sy.Year, Int(sf.Groups["s"].Value), Int(ef.Groups["e"].Value), Clean(file[(ef.Index + ef.Length)..], rest: true).Title);
        }

        // film: dal nome del file, o dalla cartella "Titolo (Anno)" se il file non ha l'anno
        var f = Clean(file);
        if (f.Year == 0 && parent.Length > 0)
        {
            var p = Clean(parent);
            if (p.Year > 0 && (f.Title.Length == 0 || SameStart(p.Title, f.Title) || GenericName().IsMatch(file))) return new NameGuess(p.Title, p.Year, null, null, "");
        }
        if (f.Title.Length == 0) f = Clean(parent);
        return new NameGuess(f.Title, f.Year, null, null, "");
    }

    static string SeriesFolder(string parent, string grand) =>
        SeasonFolder().IsMatch(parent) ? Clean(grand).Title : Clean(parent).Title;

    static bool SameStart(string a, string b)
    {
        string x = Epg.Titles.TitleKey(a), y = Epg.Titles.TitleKey(b);
        return x.Length > 0 && y.Length > 0 && (x.StartsWith(y, StringComparison.Ordinal) || y.StartsWith(x, StringComparison.Ordinal));
    }

    /// <summary>
    /// Titolo e anno da un nome con punti, trattini bassi, parentesi e sigle di rilascio. <paramref name="rest"/>: è
    /// quello che segue S01E02, dove una sigla anche all'inizio ("hdtv") vuol dire che il titolo dell'episodio non c'è.
    /// </summary>
    public static (string Title, int Year) Clean(string name, bool rest = false)
    {
        string s = Brackets().Replace(name, m => YearOnly().IsMatch(m.Value.Trim('(', ')', '[', ']', ' ')) ? " " + m.Value.Trim('(', ')', '[', ']') + " " : " ");
        s = s.Replace('_', ' ');
        // punti come separatori, ma non nelle sigle ("Mr. Bean", "S.W.A.T.") se il nome ha anche spazi
        if (!s.Contains(' ') || DotWords().IsMatch(s)) s = s.Replace('.', ' ');
        s = Spaces().Replace(s, " ").Trim();

        // l'anno: l'ultimo 19xx/20xx che non è all'inizio ("2001 Odissea nello spazio" resta un titolo)
        int year = 0, cut = s.Length;
        foreach (Match y in Year4().Matches(s))
        {
            if (y.Index == 0) continue;
            int v = Int(y.Value);
            if (v < 1900 || v > DateTime.UtcNow.Year + 1) continue;
            year = v;
            cut = y.Index;
        }
        // le sigle di rilascio chiudono il titolo
        var junk = Junk().Match(s);
        if (junk.Success && (junk.Index > 0 || rest) && junk.Index < cut) cut = junk.Index;
        string title = s[..cut].Trim(' ', '-', '–', ':', ',', '.', '_');
        return (title, year);
    }

    static int Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    [GeneratedRegex(@"(?:^|[\s._\-\[(])[Ss](?<s>\d{1,2})[\s._\-]?[Ee](?<e>\d{1,3})(?:[\s._\-]?[Ee]\d{1,3})*")]
    private static partial Regex SeasonEpisode();

    [GeneratedRegex(@"(?:^|[\s._\-\[(])(?<s>\d{1,2})x(?<e>\d{2,3})(?=$|[\s._\-\])])", RegexOptions.IgnoreCase)]
    private static partial Regex CrossEpisode();

    [GeneratedRegex(@"^(?:stagione|season|serie|staffel|saison)\s*(?<s>\d{1,2})$", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonFolder();

    [GeneratedRegex(@"(?:^|[\s._\-])(?:episodio|episode|ep|puntata|e)[\s._\-]?(?<e>\d{1,3})(?=$|[\s._\-])", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeOnly();

    [GeneratedRegex(@"\bsample\b", RegexOptions.IgnoreCase)]
    private static partial Regex SampleWord();

    [GeneratedRegex(@"^(movie|film|video|main|feature)$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericName();

    [GeneratedRegex(@"[\[(][^\])]*[\])]")]
    private static partial Regex Brackets();

    [GeneratedRegex(@"^(19|20)\d{2}$")]
    private static partial Regex YearOnly();

    [GeneratedRegex(@"\w\.\w+\.\w")]
    private static partial Regex DotWords();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"(?<!\d)(19|20)\d{2}(?!\d)")]
    private static partial Regex Year4();

    [GeneratedRegex(@"(?:^|[\s\-])(?:2160p|1080[pi]|720p|576p|480p|4k|uhd|hdr10?|dv|bluray|blu-ray|bdrip|brrip|bdremux|remux|webrip|web-dl|webdl|web|hdtv|hdrip|dvdrip|dvdscr|dvd5|dvd9|dvd|tvrip|satrip|x264|x265|h264|h\.264|h265|h\.265|hevc|avc|xvid|divx|aac|ac3|eac3|dts|dd5\.1|ddp5\.1|5\.1|7\.1|ita|eng|italian|english|multi|multisub|sub|subs|subita|proper|repack|extended|unrated|uncut|limited|internal|complete|completa)(?=$|[\s\-])", RegexOptions.IgnoreCase)]
    private static partial Regex Junk();
}
