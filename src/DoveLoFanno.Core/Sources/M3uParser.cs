using System.Text.RegularExpressions;

namespace DoveLoFanno.Sources;

/// <summary>Un canale di una playlist M3U.</summary>
public sealed record M3uEntry(string Name, string Group, string TvgId, string Logo, string Url, string UserAgent, string Referrer, bool Drm);

/// <summary>Playlist letta: canali e guida XMLTV indicata nell'intestazione (x-tvg-url o url-tvg).</summary>
public sealed record M3uPlaylist(string EpgUrl, IReadOnlyList<M3uEntry> Entries);

/// <summary>
/// Lettura delle playlist M3U, portata da ConvertFrom-M3U di RegistraTV: attributi anche attaccati senza spazi,
/// user agent come attributo http-user-agent o come #EXTVLCOPT, canali con #KODIPROP di licenza = DRM.
/// </summary>
public static partial class M3uParser
{
    public static M3uPlaylist Parse(string text)
    {
        var entries = new List<M3uEntry>();
        string epg = "";
        string? name = null, group = "", tvgId = "", logo = "", ua = "", referrer = "";
        bool drm = false, inEntry = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                string u = Attr(line, "x-tvg-url");
                if (u.Length == 0) u = Attr(line, "url-tvg");
                // alcune playlist ne elencano più d'una separate da virgole: si prende la prima
                if (u.Length > 0 && epg.Length == 0) epg = u.Split(',')[0].Trim();
            }
            else if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                inEntry = true;
                name = Name(line);
                group = Attr(line, "group-title");
                tvgId = Attr(line, "tvg-id");
                logo = Attr(line, "tvg-logo");
                ua = UaPrefix().Replace(Attr(line, "http-user-agent"), "");
                referrer = Attr(line, "http-referrer");
                drm = false;
            }
            else if (VlcUa().Match(line) is { Success: true } mu) { if (inEntry) ua = mu.Groups[1].Value.Trim(); }
            else if (VlcReferrer().Match(line) is { Success: true } mr) { if (inEntry) referrer = mr.Groups[1].Value.Trim(); }
            else if (KodiLicense().IsMatch(line)) { if (inEntry) drm = true; }
            else if (line.StartsWith('#')) { }
            else if (inEntry)
            {
                entries.Add(new M3uEntry(string.IsNullOrEmpty(name) ? line : name, group, tvgId, logo, line, ua, referrer, drm));
                inEntry = false;
            }
        }
        return new M3uPlaylist(epg, entries);
    }

    /// <summary>Valore di un attributo name="..." anche attaccato al precedente (tvg-id="x"tvg-logo="y").</summary>
    public static string Attr(string line, string name)
    {
        var m = Regex.Match(line, "(?i)(?:^|[\\s\":,])" + Regex.Escape(name) + "=\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : "";
    }

    static string Name(string line)
    {
        int i = line.LastIndexOf("\",", StringComparison.Ordinal);
        if (i >= 0) return line[(i + 2)..].Trim();
        int j = line.IndexOf(',');
        return j >= 0 ? line[(j + 1)..].Trim() : "";
    }

    /// <summary>Testo della playlist da un indirizzo http(s) o da un file locale.</summary>
    public static async Task<string> LoadTextAsync(string source, HttpClient http, CancellationToken ct = default)
    {
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, source);
            req.Headers.UserAgent.ParseAdd("DoveLoFanno");
            using var resp = await http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }
        return await File.ReadAllTextAsync(source, ct);
    }

    [GeneratedRegex("^http-user-agent=")]
    private static partial Regex UaPrefix();

    [GeneratedRegex(@"^#EXTVLCOPT:\s*http-user-agent=(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex VlcUa();

    [GeneratedRegex(@"^#EXTVLCOPT:\s*http-referr?er=(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex VlcReferrer();

    [GeneratedRegex("^#KODIPROP:.*license", RegexOptions.IgnoreCase)]
    private static partial Regex KodiLicense();
}
