using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DoveLoFanno.Ratings;

/// <summary>
/// Una piattaforma per un film in Italia: kind = subscription, free, rent o buy. Source = justwatch (watch providers di
/// TMDB) o release (originale della piattaforma, dedotto dall'uscita digitale o dalla rete d'origine quando JustWatch
/// non lo ha ancora in catalogo: succede nei primi giorni dopo l'uscita).
/// </summary>
public sealed record Provider(string Name, string Kind, string LogoPath, string Source = "justwatch");

/// <summary>Un risultato della ricerca libera (film o serie): Kind = movie o tv, locandina come percorso TMDB.</summary>
public sealed record TmdbSearchHit(int Id, string Kind, string Title, string OriginalTitle, int Year, string PosterPath, string Overview,
    double VoteAverage, int VoteCount, double Popularity, IReadOnlyList<int> GenreIds);

/// <summary>Una persona trovata con search/multi: regista, attore... (KnownFor = known_for_department, es. Directing).</summary>
public sealed record TmdbPerson(int Id, string Name, string KnownFor, double Popularity);

/// <summary>Dettagli di un film per la Scheda; immagini come percorsi TMDB (/abc.jpg).</summary>
public sealed record MovieDetails(string Overview, string PosterPath, string BackdropPath, int Runtime, string OriginalTitle, string Trailer, string WatchLink, IReadOnlyList<Provider> Providers,
    IReadOnlyList<int>? GenreIds = null);

/// <summary>
/// Client TMDB con la chiave dell'utente: chiave v3 (32 caratteri) come parametro api_key, token v4 (lungo) come
/// Bearer. Ritenta due volte su 429, errori 5xx e di rete; 401 = <see cref="TmdbKeyException"/>.
/// </summary>
public sealed class TmdbClient(HttpClient http, string apiKey) : ITmdbSearch, Library.ILibraryTmdb, ITmdbVotes
{
    const string Api = "https://api.themoviedb.org/3/";
    readonly string key = (apiKey ?? "").Trim();

    /// <summary>Media e numero dei voti TMDB di un film (movie) o di una serie (tv).</summary>
    public async Task<(double Average, int Count)> VotesAsync(int tmdbId, string kind, CancellationToken ct)
    {
        using var doc = await GetAsync((kind == "tv" ? "tv/" : "movie/") + tmdbId.ToString(CultureInfo.InvariantCulture), "", ct);
        return (Num(doc.RootElement, "vote_average"), (int)Num(doc.RootElement, "vote_count"));
    }

    Task<string> ITmdbVotes.ImdbIdAsync(int tmdbId, string kind, CancellationToken ct) =>
        kind == "tv" ? TvImdbIdAsync(tmdbId, ct) : ImdbIdAsync(tmdbId, ct);

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

    /// <summary>search/multi in italiano: film e serie insieme (le persone si scartano), nell'ordine di TMDB.</summary>
    public async Task<IReadOnlyList<TmdbSearchHit>> SearchMultiAsync(string text, CancellationToken ct) => (await SearchAllAsync(text, ct)).Titles;

    /// <summary>search/multi: titoli (film e serie) e persone (registi, attori) insieme.</summary>
    public async Task<(IReadOnlyList<TmdbSearchHit> Titles, IReadOnlyList<TmdbPerson> People)> SearchAllAsync(string text, CancellationToken ct)
    {
        using var doc = await GetAsync("search/multi", "query=" + Uri.EscapeDataString(text) + "&language=it-IT&include_adult=false", ct);
        return (ParseMulti(doc.RootElement), ParsePeople(doc.RootElement));
    }

    internal static List<TmdbSearchHit> ParseMulti(JsonElement root, string? forcedKind = null)
    {
        var res = new List<TmdbSearchHit>();
        if (!root.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array) return res;
        foreach (var m in arr.EnumerateArray())
        {
            var hit = Hit(m, forcedKind ?? Str(m, "media_type"));
            if (hit != null) res.Add(hit);
        }
        return res;
    }

    static TmdbSearchHit? Hit(JsonElement m, string kind)
    {
        if (kind is not ("movie" or "tv")) return null;
        var x = kind == "tv" ? ParseTv(m) : Parse(m);
        if (x.Id <= 0 || x.Title.Length == 0) return null;
        return new TmdbSearchHit(x.Id, kind, x.Title, x.OriginalTitle, x.Year, Str(m, "poster_path"), Str(m, "overview"),
            x.VoteAverage, x.VoteCount, Num(m, "popularity"), x.GenreIds);
    }

    internal static List<TmdbPerson> ParsePeople(JsonElement root)
    {
        var res = new List<TmdbPerson>();
        if (!root.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array) return res;
        foreach (var m in arr.EnumerateArray())
            if (Str(m, "media_type") == "person" && (int)Num(m, "id") > 0 && Str(m, "name").Length > 0)
                res.Add(new TmdbPerson((int)Num(m, "id"), Str(m, "name"), Str(m, "known_for_department"), Num(m, "popularity")));
        return res;
    }

    /// <summary>Filmografia di una persona: i titoli che ha diretto (Directed = true) e quelli in cui recita.</summary>
    public async Task<IReadOnlyList<(TmdbSearchHit Hit, bool Directed)>> PersonCreditsAsync(int personId, CancellationToken ct)
    {
        using var doc = await GetAsync("person/" + personId.ToString(CultureInfo.InvariantCulture) + "/combined_credits", "language=it-IT", ct);
        return ParseCredits(doc.RootElement);
    }

    internal static List<(TmdbSearchHit Hit, bool Directed)> ParseCredits(JsonElement root)
    {
        var res = new List<(TmdbSearchHit, bool)>();
        if (root.TryGetProperty("crew", out var crew) && crew.ValueKind == JsonValueKind.Array)
            foreach (var m in crew.EnumerateArray())
                if (Str(m, "job") == "Director" && Hit(m, Str(m, "media_type")) is { } h && !res.Any(x => x.Item1.Id == h.Id && x.Item1.Kind == h.Kind))
                    res.Add((h, true));
        if (root.TryGetProperty("cast", out var cast) && cast.ValueKind == JsonValueKind.Array)
            foreach (var m in cast.EnumerateArray())
                if (Hit(m, Str(m, "media_type")) is { } h && !res.Any(x => x.Item1.Id == h.Id && x.Item1.Kind == h.Kind))
                    res.Add((h, false));
        return res;
    }

    /// <summary>
    /// discover/movie o discover/tv con i filtri della ricerca avanzata, dai più popolari; <paramref name="monetization"/>
    /// (es. "flatrate|free|ads" o "rent|buy") limita ai titoli disponibili in Italia in quel modo.
    /// </summary>
    public async Task<IReadOnlyList<TmdbSearchHit>> DiscoverAsync(string kind, SearchFilter f, string monetization, CancellationToken ct, int page = 1)
    {
        var q = new List<string> { "language=it-IT", "include_adult=false", "sort_by=popularity.desc", "page=" + page.ToString(CultureInfo.InvariantCulture) };
        bool tv = kind == "tv";
        // genere: per i film il suo codice, per le serie gli equivalenti (Azione = Action & Adventure); i documentari = 99
        var must = new List<string>();
        if (f.Kind == "documentary") must.Add("99");
        if (f.GenreList.Count > 0)
        {
            // più generi = uno qualsiasi ("|" per TMDB); per le serie gli equivalenti, e senza equivalenti niente serie
            var ids = (tv ? f.GenreList.SelectMany(id => TmdbGenres.All.FirstOrDefault(g => g.Id == id)?.Tv ?? []) : f.GenreList).Distinct().ToList();
            if (ids.Count == 0) return [];
            must.Add(string.Join("|", ids));
        }
        if (must.Count > 0) q.Add("with_genres=" + Uri.EscapeDataString(string.Join(",", must)));
        string dateField = tv ? "first_air_date" : "primary_release_date";
        if (f.YearFrom > 0) q.Add($"{dateField}.gte={f.YearFrom}-01-01");
        if (f.YearTo > 0) q.Add($"{dateField}.lte={f.YearTo}-12-31");
        if (f.Country.Length > 0) q.Add("with_origin_country=" + Uri.EscapeDataString(f.Country.ToUpperInvariant()));
        if (f.MinRating > 0) q.Add("vote_average.gte=" + f.MinRating.ToString(CultureInfo.InvariantCulture) + "&vote_count.gte=50");
        if (monetization.Length > 0) q.Add("watch_region=IT&with_watch_monetization_types=" + Uri.EscapeDataString(monetization));
        using var doc = await GetAsync("discover/" + (tv ? "tv" : "movie"), string.Join("&", q), ct);
        return ParseMulti(doc.RootElement, tv ? "tv" : "movie");
    }

    /// <summary>Paesi di produzione (ISO): per un film production_countries, per una serie origin_country.</summary>
    public async Task<IReadOnlyList<string>> CountriesAsync(int tmdbId, string kind, CancellationToken ct)
    {
        using var doc = await GetAsync((kind == "tv" ? "tv/" : "movie/") + tmdbId.ToString(CultureInfo.InvariantCulture), "", ct);
        var res = new List<string>();
        var r = doc.RootElement;
        if (r.TryGetProperty("production_countries", out var pc) && pc.ValueKind == JsonValueKind.Array)
            res.AddRange(pc.EnumerateArray().Select(c => Str(c, "iso_3166_1")).Where(s => s.Length > 0));
        if (r.TryGetProperty("origin_country", out var oc) && oc.ValueKind == JsonValueKind.Array)
            res.AddRange(oc.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString() ?? "").Where(s => s.Length > 0));
        return res.Distinct().ToList();
    }

    /// <summary>Piattaforme in Italia di un titolo (JustWatch tramite TMDB), senza gli altri dettagli.</summary>
    public async Task<IReadOnlyList<Provider>> ProvidersAsync(int tmdbId, string kind, CancellationToken ct)
    {
        using var doc = await GetAsync((kind == "tv" ? "tv/" : "movie/") + tmdbId.ToString(CultureInfo.InvariantCulture) + "/watch/providers", "", ct);
        var res = new List<Provider>();
        if (doc.RootElement.TryGetProperty("results", out var all) && all.TryGetProperty("IT", out var it))
            foreach (var (key, k) in new[] { ("flatrate", "subscription"), ("free", "free"), ("ads", "free"), ("rent", "rent"), ("buy", "buy") })
                if (it.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var p in arr.EnumerateArray())
                        if (Str(p, "provider_name") is { Length: > 0 } name && !res.Any(x => x.Name == name && x.Kind == k))
                            res.Add(new Provider(name, k, Str(p, "logo_path")));
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
            "language=it-IT&append_to_response=videos,watch/providers,release_dates&include_video_language=it,en", ct);
        return ParseDetails(doc.RootElement);
    }

    // nomi con cui TMDB indica la piattaforma d'origine (nota dell'uscita digitale, rete di una serie) → nome della piattaforma in Italia
    static readonly Dictionary<string, string> OriginPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Prime Video"] = "Amazon Prime Video", ["Amazon Prime Video"] = "Amazon Prime Video", ["Amazon"] = "Amazon Prime Video",
        ["Netflix"] = "Netflix", ["Disney+"] = "Disney Plus", ["Disney Plus"] = "Disney Plus",
        ["Apple TV+"] = "Apple TV", ["Apple TV"] = "Apple TV", ["Paramount+"] = "Paramount Plus", ["Paramount Plus"] = "Paramount Plus",
    };

    /// <summary>
    /// Piattaforma d'origine di un originale (Prime Video, Netflix...), già uscito: per un film dalla nota dell'uscita digitale
    /// (tipo 4), prima quella italiana; per una serie dalla rete d'origine. null se non è un originale di una piattaforma nota.
    /// </summary>
    internal static string? OriginPlatform(JsonElement m, DateTimeOffset now)
    {
        if (m.TryGetProperty("release_dates", out var rd) && rd.TryGetProperty("results", out var countries) && countries.ValueKind == JsonValueKind.Array)
        {
            var found = new List<(bool It, string Name)>();
            foreach (var c in countries.EnumerateArray())
                if (c.TryGetProperty("release_dates", out var dates) && dates.ValueKind == JsonValueKind.Array)
                    foreach (var d in dates.EnumerateArray())
                        if ((int)Num(d, "type") == 4 && OriginPlatforms.TryGetValue(Str(d, "note").Trim(), out var name)
                            && DateTimeOffset.TryParse(Str(d, "release_date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when) && when <= now)
                            found.Add((Str(c, "iso_3166_1") == "IT", name));
            if (found.Count > 0) return found.OrderByDescending(x => x.It).First().Name;
        }
        if (m.TryGetProperty("networks", out var nets) && nets.ValueKind == JsonValueKind.Array
            && DateTimeOffset.TryParse(Str(m, "first_air_date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var first) && first <= now)
            foreach (var n in nets.EnumerateArray())
                if (OriginPlatforms.TryGetValue(Str(n, "name").Trim(), out var name)) return name;
        return null;
    }

    internal static MovieDetails ParseDetails(JsonElement m) => ParseDetails(m, DateTimeOffset.UtcNow);

    internal static MovieDetails ParseDetails(JsonElement m, DateTimeOffset now)
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
        // un originale appena uscito che JustWatch non ha ancora in catalogo: la piattaforma d'origine, segnata come dedotta.
        // Solo se JustWatch non indica nessun abbonamento in Italia: quando c'è, vale il suo dato (diritti diversi per paese)
        if (!providers.Any(p => p.Kind == "subscription") && OriginPlatform(m, now) is { } origin)
            providers.Add(new Provider(origin, "subscription", "", "release"));
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

    /// <summary>
    /// Piattaforme di un paese (film e serie insieme) con logo e ordine di rilievo di JustWatch: 0 = la più diffusa.
    /// </summary>
    public async Task<IReadOnlyList<PlatformEntry>> WatchProvidersAsync(string region, CancellationToken ct)
    {
        var res = new Dictionary<string, PlatformEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (string kind in new[] { "movie", "tv" })
        {
            using var doc = await GetAsync("watch/providers/" + kind, "watch_region=" + region + "&language=it-IT", ct);
            if (!doc.RootElement.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var p in arr.EnumerateArray())
            {
                string name = Str(p, "provider_name").Trim();
                if (name.Length == 0) continue;
                int prio = p.TryGetProperty("display_priorities", out var dp) && dp.ValueKind == JsonValueKind.Object && dp.TryGetProperty(region, out _)
                    ? (int)Num(dp, region) : 500 + (int)Num(p, "display_priority");
                if (!res.TryGetValue(name, out var old) || prio < old.Priority) res[name] = new PlatformEntry(name, Str(p, "logo_path"), prio);
            }
        }
        return [.. res.Values];
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
