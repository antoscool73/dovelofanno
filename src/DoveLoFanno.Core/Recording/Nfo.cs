using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DoveLoFanno.Recording;

/// <summary>Dati dell'opera per il .nfo e per i nomi dei file che Jellyfin, Plex ed Emby si aspettano.</summary>
public sealed record MediaInfo(
    string Title, int Year = 0, string Plot = "", IReadOnlyList<string>? Genres = null,
    int? TmdbId = null, string ImdbId = "", string Channel = "", DateTimeOffset? Aired = null,
    string SeriesTitle = "", int? Season = null, int? Episode = null, string Directors = "", string Actors = "")
{
    public bool IsEpisode => SeriesTitle.Length > 0 && Season is not null && Episode is not null;
}

/// <summary>
/// Nomi delle cartelle e file .nfo nello stile Kodi, letto da Jellyfin, Plex (con l'agente adatto) ed Emby:
/// film in "Titolo (Anno)/Titolo (Anno).mp4", episodi in "Serie/Stagione 01/Serie - S01E02 - Titolo.mp4".
/// </summary>
public static class Nfo
{
    /// <summary>Cartella (relativa alla cartella delle registrazioni) e nome del file senza estensione.</summary>
    public static (string Folder, string BaseName) LibraryPath(MediaInfo m)
    {
        if (m.IsEpisode)
        {
            string series = SafeName(m.SeriesTitle);
            string name = $"{series} - S{m.Season:D2}E{m.Episode:D2}";
            if (m.Title.Length > 0 && !string.Equals(m.Title, m.SeriesTitle, StringComparison.OrdinalIgnoreCase)) name += " - " + SafeName(m.Title);
            return (Path.Combine(series, $"Stagione {m.Season:D2}"), name);
        }
        string film = m.Year > 0 ? $"{SafeName(m.Title)} ({m.Year})" : SafeName(m.Title);
        return (film, film);
    }

    public static XDocument Build(MediaInfo m)
    {
        var root = new XElement(m.IsEpisode ? "episodedetails" : "movie",
            new XElement("title", m.Title));
        if (m.IsEpisode)
        {
            root.Add(new XElement("showtitle", m.SeriesTitle), new XElement("season", m.Season), new XElement("episode", m.Episode));
        }
        if (m.Year > 0) root.Add(new XElement("year", m.Year));
        if (m.Plot.Length > 0) root.Add(new XElement("plot", m.Plot));
        foreach (string g in m.Genres ?? []) root.Add(new XElement("genre", g));
        foreach (string d in Split(m.Directors)) root.Add(new XElement("director", d));
        foreach (string a in Split(m.Actors)) root.Add(new XElement("actor", new XElement("name", a)));
        if (m.TmdbId is int t) root.Add(new XElement("uniqueid", new XAttribute("type", "tmdb"), new XAttribute("default", "true"), t));
        if (m.ImdbId.Length > 0) root.Add(new XElement("uniqueid", new XAttribute("type", "imdb"), m.ImdbId));
        if (m.Channel.Length > 0) root.Add(new XElement("studio", m.Channel));
        if (m.Aired is DateTimeOffset d0) root.Add(new XElement(m.IsEpisode ? "aired" : "premiered", d0.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    public static void Write(MediaInfo m, string path)
    {
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };
        using var w = XmlWriter.Create(path, settings);
        Build(m).Save(w);
    }

    static IEnumerable<string> Split(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Nome valido su Windows e Linux (da Get-SafeName): caratteri vietati = '_', niente punto finale.</summary>
    public static string SafeName(string s)
    {
        var bad = new HashSet<char>(Path.GetInvalidFileNameChars()) { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(bad.Contains(c) || char.IsControl(c) ? '_' : c);
        string r = sb.ToString().Trim().TrimEnd('.');
        return r.Length == 0 ? "Registrazione" : r;
    }
}
