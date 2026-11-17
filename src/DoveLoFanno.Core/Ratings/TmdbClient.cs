using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DoveLoFanno.Ratings;

/// <summary>Una piattaforma per un film in Italia: kind = subscription, free, rent o buy (dati JustWatch tramite TMDB).</summary>
public sealed record Provider(string Name, string Kind, string LogoPath);

/// <summary>Dettagli di un film per la Scheda; immagini come percorsi TMDB (/abc.jpg).</summary>
public sealed record MovieDetails(string Overview, string PosterPath, string BackdropPath, int Runtime, string OriginalTitle, string Trailer, string WatchLink, IReadOnlyList<Provider> Providers,
    IReadOnlyList<int>? GenreIds = null);

/// <summary>
/// Client TMDB con la chiave dell'utente: chiave v3 (32 caratteri) come parametro api_key, token v4 (lungo) come
/// Bearer. Ritenta due volte su 429, errori 5xx e di rete; 401 = <see cref="TmdbKeyException"/>.
/// </summary>
public sealed class TmdbClient(HttpClient http, string apiKey) : ITmdbSearch, Library.ILibraryTmdb
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

    /// <summary>search/tv in italiano (serie e documentari a puntate): il nome della serie va in Title, la prima messa in onda in ReleaseDate.</summary>
    public async Task<IReadOnlyList<TmdbMovie>> SearchTvAsync(string title, int year, CancellationToken ct)
    {
        string q = "query=" + Uri.EscapeDataString(title) + "&language=it-IT&include_adult=false";
        if (year > 0) q += "&first_air_date_year=" + year.ToString(CultureInfo.InvariantCulture);
        using var doc = await GetAsync("search/tv", q, ct);
        var res = new List<TmdbMovie>();
        if (doc.RootElement.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var m in arr.EnumerateArray()) res.Add(ParseTv(m));
        return res;
    }

    public async Task<string> TvImdbIdAsync(int tmdbId, CancellationToken ct)
    {
        using var doc = await GetAsync("tv/" + tmdbId.ToString(CultureInfo.InvariantCulture) + "/external_ids", "", ct);
        return Str(doc.RootElement, "imdb_id");
    }

    /// <summary>Titoli degli episodi di una stagione (numero → titolo in italiano, se TMDB lo ha).</summary>
    public async Task<IReadOnlyDictionary<int, string>> SeasonAsync(int tmdbId, int season, CancellationToken ct)
    {
        using var doc = await GetAsync("tv/" + tmdbId.ToString(CultureInfo.InvariantCulture) + "/season/" + season.ToString(CultureInfo.InvariantCulture), "language=it-IT", ct);
        var res = new Dictionary<int, string>();
        if (doc.RootElement.TryGetProperty("episodes", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
            {
                int n = (int)Num(e, "episode_number");
                string name = Str(e, "name");
                // TMDB senza traduzione mette "Episodio 3": non è un titolo
                if (n > 0 && name.Length > 0 && !name.StartsWith("Episodio ", StringComparison.OrdinalIgnoreCase)) res[n] = name;
            }
        return res;
    }

    /// <summary>Come <see cref="MovieDetailsAsync"/> per una serie: durata = durata tipica di un episodio.</summary>
    public async Task<MovieDetails> TvDetailsAsync(int tmdbId, CancellationToken ct)
    {
        using var doc = await GetAsync("tv/" + tmdbId.ToString(CultureInfo.InvariantCulture),
            "language=it-IT&append_to_response=videos,watch/providers&include_video_language=it,en", ct);
        return ParseDetails(doc.RootElement);
    }

    public async Task<string> ImdbIdAsync(int tmdbId, CancellationToken ct)
    {
        using var doc = await GetAsync("movie/" + tmdbId.ToString(CultureInfo.InvariantCulture) + "/external_ids", "", ct);
        return Str(doc.RootElement, "imdb_id");
    }

    /// <summary>Trama in italiano, immagini, durata, trailer e piattaforme in Italia, in una sola chiamata.</summary>
    public async Task<MovieDetails> MovieDetailsAsync(int tmdbId, CancellationToken ct)
    {
        using var doc = await GetAsync("movie/" + tmdbId.ToString(CultureInfo.InvariantCulture),
            "language=it-IT&append_to_response=videos,watch/providers&include_video_language=it,en", ct);
        return ParseDetails(doc.RootElement);
    }

    internal static MovieDetails ParseDetails(JsonElement m)
    {
        string trailer = "";
        if (m.TryGetProperty("videos", out var v) && v.TryGetProperty("results", out var vids) && vids.ValueKind == JsonValueKind.Array)
        {
            var yt = vids.EnumerateArray()
                .Where(x => Str(x, "site") == "YouTube" && Str(x, "type") == "Trailer" && Str(x, "key").Length > 0)
                .OrderBy(x => Str(x, "iso_639_1") == "it" ? 0 : 1)
                .ThenBy(x => x.TryGetProperty("official", out var o) && o.ValueKind == JsonValueKind.True ? 0 : 1)
                .FirstOrDefault();
            if (yt.ValueKind == JsonValueKind.Object) trailer = "https://www.youtube.com/watch?v=" + Str(yt, "key");
        }
        var providers = new List<Provider>();
        string link = "";
        if (m.TryGetProperty("watch/providers", out var wp) && wp.TryGetProperty("results", out var res) && res.TryGetProperty("IT", out var it))
        {
            link = Str(it, "link");
            foreach (var (key, kind) in new[] { ("flatrate", "subscription"), ("free", "free"), ("ads", "free"), ("rent", "rent"), ("buy", "buy") })
                if (it.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var p in arr.EnumerateArray())
                    {
                        string name = Str(p, "provider_name");
                        if (name.Length > 0 && !providers.Any(x => x.Name == name && x.Kind == kind)) providers.Add(new Provider(name, kind, Str(p, "logo_path")));
                    }
        }
        int runtime = (int)Num(m, "runtime");
        if (runtime == 0 && m.TryGetProperty("episode_run_time", out var ert) && ert.ValueKind == JsonValueKind.Array)
            runtime = ert.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).FirstOrDefault();
        string original = Str(m, "original_title");
        if (original.Length == 0) original = Str(m, "original_name");
        var genres = m.TryGetProperty("genres", out var gs) && gs.ValueKind == JsonValueKind.Array
            ? gs.EnumerateArray().Select(g => (int)Num(g, "id")).Where(x => x > 0).ToList() : [];
        return new MovieDetails(Str(m, "overview"), Str(m, "poster_path"), Str(m, "backdrop_path"), runtime,
            original, trailer, link, providers, genres);
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
        Num(m, "vote_average"), (int)Num(m, "vote_count")) { GenreIds = Genres(m) };

    internal static TmdbMovie ParseTv(JsonElement m) => new(
        (int)Num(m, "id"), Str(m, "name"), Str(m, "original_name"), Str(m, "first_air_date"),
        Num(m, "vote_average"), (int)Num(m, "vote_count")) { GenreIds = Genres(m) };

    static IReadOnlyList<int> Genres(JsonElement m) =>
        m.TryGetProperty("genre_ids", out var g) && g.ValueKind == JsonValueKind.Array
            ? g.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToList()
            : [];

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
