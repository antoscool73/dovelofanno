using DoveLoFanno.Ratings;

namespace DoveLoFanno.Server;

/// <summary>
/// Voti dei film: dopo ogni caricamento della guida, ogni 6 ore e quando cambiano le impostazioni cerca i film nuovi
/// e scrive i voti nelle opere. La chiave TMDB è quella dell'utente (impostazione TmdbKey, mai restituita dall'API);
/// i voti IMDb sono facoltativi (ImdbRatings = 1) perché la licenza del file è solo personale.
/// </summary>
public sealed class RatingsHost(ServerContext ctx, ILogger<RatingsHost> log) : BackgroundService
{
    readonly SemaphoreSlim wake = new(0);
    readonly Lock sync = new();
    RatingService? service;
    string serviceKey = "";

    public void Kick() => wake.Release();

    /// <summary>Le impostazioni sono cambiate: il servizio si ricostruisce al prossimo giro.</summary>
    public void Reset()
    {
        lock (sync) service = null;
        wake.Release();
    }

    public string Status => Current().Status;
    public string Error => Current().Error;
    public (int Searched, int Found) Counts => Current().Counts;

    RatingService Current()
    {
        string key = ctx.Settings.Get("TmdbKey");
        bool imdb = ctx.Settings.Get("ImdbRatings") == "1";
        string id = key + "|" + imdb;
        lock (sync)
        {
            if (service == null || serviceKey != id)
            {
                var tmdb = key.Length > 0 ? new TmdbClient(ctx.Http, key) : null;
                service = new RatingService(tmdb, Path.Combine(ctx.DataDir, "ratings"), imdb ? ctx.Http : null, ctx.Clock) { UseImdb = imdb };
                serviceKey = id;
            }
            return service;
        }
    }

    /// <summary>
    /// Locandine, trame e piattaforme dei film in guida nei prossimi 8 giorni, da TMDB: così Stasera e la Guida mostrano la
    /// locandina vera e "dove lo vedo" è pronto senza aprire la Scheda. Al massimo 4 richieste al secondo.
    /// </summary>
    async Task DetailsAsync(CancellationToken ct)
    {
        string key = ctx.Settings.Get("TmdbKey");
        if (key.Length == 0) return;
        var tmdb = new TmdbClient(ctx.Http, key);
        int done = 0;
        foreach (var (workId, tmdbId, kind) in ctx.Works.WorksNeedingDetails(ctx.Clock.GetUtcNow(), 8, 600))
        {
            try
            {
                var d = kind == "tv" ? await tmdb.TvDetailsAsync((int)tmdbId, ct) : await tmdb.MovieDetailsAsync((int)tmdbId, ct);
                ctx.Works.SaveDetails(workId, d, ctx.Clock.GetUtcNow());
                done++;
            }
            catch (TmdbKeyException) { return; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
            {
                log.LogWarning("Dettagli TMDB dell'opera {Id} non scaricati: {Error}", workId, ex.Message);
            }
            await Task.Delay(250, ct);
        }
        if (done > 0) log.LogInformation("Dettagli TMDB (locandine, piattaforme): {Count} opere", done);
    }

    /// <summary>
    /// Voti dei titoli nelle liste e nella mia serata che non passano in guida (es. un originale trovato con la lente):
    /// voto IMDb dal file se i voti IMDb sono attivi, altrimenti TMDB da 20 voti; ogni giro del servizio (6 ore).
    /// </summary>
    async Task ListRatingsAsync(CancellationToken ct)
    {
        string key = ctx.Settings.Get("TmdbKey");
        var tmdb = key.Length > 0 ? new TmdbClient(ctx.Http, key) : null;
        string? file = ctx.Settings.Get("ImdbRatings") == "1" ? Path.Combine(ctx.DataDir, "ratings", "title.ratings.tsv.gz") : null;
        int n = await ListRatingSync.RunAsync(ctx.Works, tmdb, file, ct, pace: TimeSpan.FromMilliseconds(250));
        if (n > 0) log.LogInformation("Voti dei titoli nelle liste: {Count} opere", n);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int rated = await RatingSync.RunAsync(ctx.Guide, Current(), ctx.Clock.GetUtcNow(), ct);
                if (rated > 0) log.LogInformation("Voti dei film: {Rated} opere con voto", rated);
                await DetailsAsync(ct);
                await ListRatingsAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Errore nei voti dei film");
            }
            try { await wake.WaitAsync(TimeSpan.FromHours(6), ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}
