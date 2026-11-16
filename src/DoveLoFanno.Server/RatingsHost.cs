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

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int rated = await RatingSync.RunAsync(ctx.Guide, Current(), ctx.Clock.GetUtcNow(), ct);
                if (rated > 0) log.LogInformation("Voti dei film: {Rated} opere con voto", rated);
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
