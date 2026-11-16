using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DoveLoFanno.Ratings;

/// <summary>
/// Client TMDB con la chiave dell'utente: chiave v3 (32 caratteri) come parametro api_key, token v4 (lungo) come
/// Bearer. Ritenta due volte su 429, errori 5xx e di rete; 401 = <see cref="TmdbKeyException"/>.
/// </summary>
public sealed class TmdbClient(HttpClient http, string apiKey) : ITmdbSearch
{
    const string Api = "https://api.themoviedb.org/3/";
    readonly string key = (apiKey ?? "").Trim();

    public bool HasKey => key.Length > 0;

    public async Task<IReadOnlyList<TmdbMovie>> SearchMovieAsync(string title, int year, CancellationToken ct)
    {
        string q = "query=" + Uri.EscapeDataString(title) + "&language=it-IT&include_adult=false";
        if (year > 0) q += "&year=" + year.ToString(CultureInfo.InvariantCulture);
        using var doc = await GetAsync("search/movie", q, ct);
        var res = new List<TmdbMovie>();
        if (doc.RootElement.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var m in arr.EnumerateArray()) res.Add(Parse(m));
        return res;
    }

    public async Task<string> ImdbIdAsync(int tmdbId, CancellationToken ct)
    {
        using var doc = await GetAsync("movie/" + tmdbId.ToString(CultureInfo.InvariantCulture) + "/external_ids", "", ct);
        return Str(doc.RootElement, "imdb_id");
    }

    /// <summary>"" se la chiave funziona, altrimenti il motivo.</summary>
    public async Task<string> TestKeyAsync(CancellationToken ct = default)
    {
        if (key.Length == 0) return "chiave vuota";
        try { using var _ = await GetAsync("configuration", "", ct); return ""; }
        catch (Exception ex) when (ex is TmdbKeyException or HttpRequestException or TaskCanceledException) { return ex.Message; }
    }

    internal static TmdbMovie Parse(JsonElement m) => new(
        (int)Num(m, "id"), Str(m, "title"), Str(m, "original_title"), Str(m, "release_date"),
        Num(m, "vote_average"), (int)Num(m, "vote_count"));

    async Task<JsonDocument> GetAsync(string path, string query, CancellationToken ct)
    {
        bool bearer = key.Length > 40;
        string url = Api + path + "?" + query;
        if (!bearer) url += (query.Length > 0 ? "&" : "") + "api_key=" + Uri.EscapeDataString(key);
        for (int attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("DoveLoFanno");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (bearer) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            HttpResponseMessage? resp = null;
            try
            {
                resp = await http.SendAsync(req, ct);
                if (resp.StatusCode == HttpStatusCode.Unauthorized) throw new TmdbKeyException();
                int code = (int)resp.StatusCode;
                if ((code == 429 || code >= 500) && attempt < 2) { await Task.Delay(2000 * (attempt + 1), ct); continue; }
                resp.EnsureSuccessStatusCode();
                await using var s = await resp.Content.ReadAsStreamAsync(ct);
                return await JsonDocument.ParseAsync(s, cancellationToken: ct);
            }
            catch (HttpRequestException) when (resp == null && attempt < 2)
            {
                await Task.Delay(2000 * (attempt + 1), ct);
            }
            finally { resp?.Dispose(); }
        }
    }

    static string Str(JsonElement m, string k) =>
        m.TryGetProperty(k, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => "",
        } : "";

    static double Num(JsonElement m, string k)
    {
        if (!m.TryGetProperty(k, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
        return 0;
    }
}
