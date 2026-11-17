using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DoveLoFanno.Recording;

/// <summary>Quello che si sa dell'opera di una registrazione, dalla guida e da TMDB.</summary>
public sealed record RecordingMedia(
    string Kind, string WorkTitle, int Year, int? TmdbId, string TmdbKind, string ImdbId, string Overview,
    string Description, string CategoriesJson, string Directors, string Actors, string Episode, string Subtitle, string Group);

/// <summary>
/// Dove va una registrazione dentro la cartella delle registrazioni, con i nomi che Jellyfin, Plex ed Emby si
/// aspettano: "Film/Titolo (Anno)/Titolo (Anno).mp4" con il .nfo, "Serie TV/Serie/Stagione 01/Serie - S01E02 - Titolo.mp4",
/// "Programmi/Titolo/Titolo 2026-11-17 21.30.mp4" (riconosciuti per data), "Audio/..." per il solo audio.
/// In Jellyfin: una libreria Film su "Film", una Serie su "Serie TV" e "Programmi".
/// </summary>
public static partial class LibraryLayout
{
    public const string Films = "Film", Series = "Serie TV", Programmes = "Programmi", Audio = "Audio";

    /// <summary>Cartella (relativa) e nome del file senza estensione; Info per il .nfo (null = nessun .nfo).</summary>
    public static (string Folder, string BaseName, MediaInfo? Info) For(string title, string channel, DateTimeOffset start, string format,
        RecordingMedia? m, string recordingsDir)
    {
        string local = start.ToLocalTime().ToString("yyyy-MM-dd HH.mm", CultureInfo.InvariantCulture);
        if (format == "mp3")
            return (Path.Combine(Audio, Nfo.SafeName(title)), Nfo.SafeName($"{title} {local}"), null);

        var genres = Genres(m?.CategoriesJson);
        string plot = m == null ? "" : m.Overview.Length > 0 ? m.Overview : m.Description;
        bool film = m != null && (m.Kind == "film" || m.Group == "Film");
        if (film)
        {
            string t = m!.WorkTitle.Length > 0 ? m.WorkTitle : title;
            var info = new MediaInfo(t, m.Year, plot, genres, m.TmdbKind == "movie" ? m.TmdbId : null, m.ImdbId, channel, start,
                Directors: m.Directors, Actors: m.Actors);
            var (folder, name) = Nfo.LibraryPath(info);
            // un secondo passaggio dello stesso film: Jellyfin lo mostra come un'altra versione
            if (File.Exists(Path.Combine(recordingsDir, Films, folder, name + ".mp4"))) name += " - " + Nfo.SafeName($"{channel} {local}");
            return (Path.Combine(Films, folder), name, info);
        }
        if (m != null && ParseEpisode(m.Episode) is (int season, int episode))
        {
            string series = m.WorkTitle.Length > 0 ? m.WorkTitle : title;
            var info = new MediaInfo(m.Subtitle, 0, plot, genres, null, "", channel, start, series, season, episode, m.Directors, m.Actors);
            var (folder, name) = Nfo.LibraryPath(info);
            return (Path.Combine(Series, folder), name, info);
        }
        string show = m != null && m.WorkTitle.Length > 0 ? m.WorkTitle : title;
        return (Path.Combine(Programmes, Nfo.SafeName(show)), Nfo.SafeName($"{show} {local}"), null);
    }

    /// <summary>"S2 E5", "S02E05", "Stagione 2 Ep. 5", "2x05" diventano (2, 5); senza stagione null.</summary>
    public static (int Season, int Episode)? ParseEpisode(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = SeasonEpisode().Match(s);
        if (!m.Success) m = Cross().Match(s);
        if (!m.Success) return null;
        int season = int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture), episode = int.Parse(m.Groups["e"].Value, CultureInfo.InvariantCulture);
        return season > 0 && episode > 0 ? (season, episode) : null;
    }

    static List<string> Genres(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    [GeneratedRegex(@"(?:\bS|\bstagione|\bseason)\s*\.?\s*(?<s>\d{1,2})\D{0,6}?(?:episodio|episode|puntata|ep|e)\s*\.?\s*(?<e>\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonEpisode();

    [GeneratedRegex(@"\b(?<s>\d{1,2})x(?<e>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex Cross();
}
