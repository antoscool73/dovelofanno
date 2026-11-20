using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using DoveLoFanno.Data;
using DoveLoFanno.Sources;

namespace DoveLoFanno.Server;

/// <summary>
/// Avvisi e notifiche: ogni minuto controlla gli avvisi (e programma le registrazioni automatiche), ogni ora le proposte
/// nuove di ogni profilo, una volta al giorno le piattaforme delle opere con un avviso su abbonamenti o noleggio. Le
/// registrazioni finite diventano notifiche per tutta la famiglia. Le notifiche si inoltrano anche su Telegram, se
/// configurato, fuori dagli orari di silenzio.
/// </summary>
public sealed class NotifyHost(ServerContext ctx, ILogger<NotifyHost> log) : BackgroundService
{
    DateTimeOffset lastProposals = DateTimeOffset.MinValue;
    DateTimeOffset lastOnline = DateTimeOffset.MinValue;
    DateTimeOffset lastSeries = DateTimeOffset.MinValue;

    /// <summary>«Registra la serie»: le puntate nuove che la guida riporta si programmano da sole, con una notifica.</summary>
    void RunSeries(DateTimeOffset now)
    {
        foreach (var c in ctx.Series.CheckAll(now, SeriesApi.Defaults(ctx)))
        {
            var first = c.Episodes.Where(e => e.Status == "scheduled").Select(e => e.Programme).OrderBy(p => p.Start).First();
            string text = c.NewlyScheduled == 1
                ? $"{first.Start.ToLocalTime().ToString("ddd d MMM HH:mm", CultureInfo.GetCultureInfo("it-IT"))} su {first.ChannelName}"
                : $"{c.NewlyScheduled} puntate nuove, la prima {first.Start.ToLocalTime().ToString("ddd d MMM HH:mm", CultureInfo.GetCultureInfo("it-IT"))}";
            ctx.Notifications.Add(c.Rule.ProfileId, "recording", $"Serie: «{c.Rule.Title}» programmata", text, "/registrazioni", now);
            log.LogInformation("Serie {Title}: {Count} puntate programmate", c.Rule.Title, c.NewlyScheduled);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        ctx.Scheduler.Finished += OnFinished;
        ctx.Notifications.Added += n => _ = ForwardAsync(n);
        foreach (string note in ctx.ConvertedPreferences) log.LogWarning("Preferenza convertita in lista dinamica: {Note}", note);
        if (ctx.MergedChannels > 0) log.LogWarning("Canali doppi uniti all'avvio: {Count}", ctx.MergedChannels);
        try { await Task.Delay(TimeSpan.FromSeconds(20), ctx.Clock, ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            var now = ctx.Clock.GetUtcNow();
            try { RunAlerts(now); }
            catch (Exception ex) { log.LogError(ex, "Errore nel controllo degli avvisi"); }
            if (now - lastProposals > TimeSpan.FromHours(1))
            {
                lastProposals = now;
                try { await RunProposalsAsync(now, ct); }
                catch (Exception ex) { log.LogError(ex, "Errore nel calcolo delle proposte"); }
            }
            if (now - lastSeries > TimeSpan.FromMinutes(30))
            {
                lastSeries = now;
                try { RunSeries(now); }
                catch (Exception ex) { log.LogError(ex, "Errore nel controllo delle serie"); }
            }
            if (now - lastOnline > TimeSpan.FromHours(6))
            {
                lastOnline = now;
                try { await RefreshOnlineAsync(now, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Piattaforme non aggiornate: {Error}", ex.Message); }
            }
            try { await Task.Delay(TimeSpan.FromMinutes(1), ctx.Clock, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Ricalcolo delle proposte al prossimo giro (preferenze cambiate).</summary>
    public void ProposalsChanged() => lastProposals = DateTimeOffset.MinValue;

    void OnFinished(long id, Recording.RecordingOutcome o)
    {
        var r = ctx.Recordings.Get(id);
        string title = r?.Title ?? "Registrazione";
        var now = ctx.Clock.GetUtcNow();
        switch (o.State)
        {
            case "completed":
                ctx.Notifications.Add(null, "recording", $"Registrata: «{title}»", o.MergeFailed ? "Unione in MP4 non riuscita: restano gli spezzoni" : r?.ChannelName ?? "", "/registrazioni", now);
                break;
            case "incomplete":
                ctx.Notifications.Add(null, "recording", $"Registrazione incompleta: «{title}»", $"Mancano {o.LostMinutes} minuti", "/registrazioni", now);
                break;
            case "stopped":
                break;   // fermata a mano: chi l'ha fermata lo sa già
            default:
                ctx.Notifications.Add(null, "recording", $"Registrazione non riuscita: «{title}»", "Il motivo è nell'elenco delle registrazioni", "/registrazioni", now);
                break;
        }
    }

    void RunAlerts(DateTimeOffset now)
    {
        var fired = ctx.Alerts.Check(now, ctx.Clock.LocalTimeZone, (p, profileId) =>
        {
            if (p.ChannelId is not long ch) return null;
            int before = ctx.Settings.GetInt("BeforeMin"), after = ctx.Settings.GetInt("AfterMin");
            if (p.Stop.AddMinutes(after) <= now) return null;
            return ctx.Recordings.Schedule(ch, p.Title, p.Start, p.Stop, before, after, ctx.Settings.Get("Format"), now, p.WorkId, profileId, p.Id);
        });
        foreach (var f in fired)
        {
            ctx.Notifications.Add(f.ProfileId, "alert", f.Title, f.Text, f.Link, now);
            log.LogInformation("Avviso: {Title} ({Text})", f.Title, f.Text);
        }
    }

    /// <summary>
    /// Proposte (19/11/2026): per le liste dinamiche che il profilo ha nella pagina principale, i titoli nuovi che passano
    /// in TV nei prossimi 2 giorni (la stessa ricerca della pagina Cerca, solo tra le opere note, senza «Non proporre più»).
    /// </summary>
    async Task RunProposalsAsync(DateTimeOffset now, CancellationToken ct)
    {
        foreach (var g in ctx.Lists.HomeDynamic().GroupBy(x => x.ProfileId))
        {
            if (ctx.Profiles.Get(g.Key) == null) continue;
            var found = new Dictionary<string, string>();          // opera@passaggio -> titolo
            foreach (var l in g)
            {
                var r = await SearchApi.RunAsync(ctx, (l.Query.Q ?? "").Trim(), l.Query.ToFilter(), g.Key, 400, dropExcluded: true, useTmdb: false, ct);
                foreach (var h in r.Hits.Where(h => h.WorkId != null && h.NextAiring is { } t && t > now && t < now.AddDays(2)))
                    found.TryAdd($"{h.WorkId}@{h.NextAiring!.Value.ToUnixTimeSeconds()}", h.Title);
            }
            var fresh = ctx.Preferences.TakeNewKeys(g.Key, "L:", found.Keys, now);
            if (fresh.Count == 0) continue;
            var titles = fresh.Select(k => found[k]).Distinct().ToList();
            string names = string.Join(", ", titles.Take(3)) + (titles.Count > 3 ? $" e altri {titles.Count - 3}" : "");
            ctx.Notifications.Add(g.Key, "proposals", titles.Count == 1 ? "Una nuova proposta per te" : $"{titles.Count} nuove proposte per te", names, "/#proposte", now);
        }
    }

    /// <summary>Piattaforme (JustWatch tramite TMDB) delle opere con un avviso su abbonamenti o noleggio, una volta al giorno.</summary>
    async Task RefreshOnlineAsync(DateTimeOffset now, CancellationToken ct)
    {
        string key = ctx.Settings.Get("TmdbKey");
        if (key.Length == 0) return;
        var tmdb = new Ratings.TmdbClient(ctx.Http, key);
        foreach (long id in ctx.Alerts.WorksToWatchOnline())
        {
            if (ctx.Works.DetailsToRefresh(id, now) is not { } t) continue;
            try { ctx.Works.SaveDetails(id, t.Kind == "tv" ? await tmdb.TvDetailsAsync((int)t.TmdbId, ct) : await tmdb.MovieDetailsAsync((int)t.TmdbId, ct), now); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or Ratings.TmdbKeyException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
            {
                log.LogWarning("Dettagli TMDB dell'opera {Id} non aggiornati: {Error}", id, ex.Message);
            }
        }
    }

    async Task ForwardAsync(NotificationRow n)
    {
        string token = ctx.Settings.Get("TelegramToken"), chat = ctx.Settings.Get("TelegramChat");
        if (token.Length == 0 || chat.Length == 0 || Quiet(ctx.Settings, ctx.Clock.GetLocalNow())) return;
        try { await Telegram.SendAsync(ctx.Http, token, chat, n.Title + (n.Text.Length > 0 ? "\n" + n.Text : ""), CancellationToken.None); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { log.LogWarning("Telegram non raggiunto: {Error}", ex.Message); }
    }

    /// <summary>Vero negli orari di silenzio (QuietFrom-QuietTo, anche a cavallo della mezzanotte).</summary>
    public static bool Quiet(SettingsStore s, DateTimeOffset localNow)
    {
        if (!TimeOnly.TryParse(s.Get("QuietFrom"), CultureInfo.InvariantCulture, out var a) || !TimeOnly.TryParse(s.Get("QuietTo"), CultureInfo.InvariantCulture, out var b) || a == b)
            return false;
        var t = TimeOnly.FromDateTime(localNow.DateTime);
        return a < b ? t >= a && t < b : t >= a || t < b;
    }
}

static class Telegram
{
    public static async Task SendAsync(HttpClient http, string token, string chat, string text, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        using var resp = await http.PostAsJsonAsync($"https://api.telegram.org/bot{token}/sendMessage", new { chat_id = chat, text }, cts.Token);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"Telegram ha risposto {(int)resp.StatusCode}");
    }
}

/// <summary>
/// Anteprima live muta nel browser: un fotogramma JPEG del canale, preso con ffmpeg dal primo flusso utilizzabile e tenuto
/// 8 secondi (più finestre aperte sullo stesso canale non moltiplicano le connessioni). Non tocca le sorgenti con un
/// limite di connessioni mentre si registra.
/// </summary>
public sealed class PreviewService(ServerContext ctx)
{
    readonly ConcurrentDictionary<long, (DateTimeOffset At, byte[] Jpeg)> cache = new();
    readonly ConcurrentDictionary<long, SemaphoreSlim> locks = new();

    /// <summary>Il fotogramma, oppure null con il motivo.</summary>
    public async Task<(byte[]? Jpeg, string Why)> GetAsync(long channelId, CancellationToken ct)
    {
        if (cache.TryGetValue(channelId, out var hit) && ctx.Clock.GetUtcNow() - hit.At < TimeSpan.FromSeconds(8)) return (hit.Jpeg, "");
        var gate = locks.GetOrAdd(channelId, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(channelId, out hit) && ctx.Clock.GetUtcNow() - hit.At < TimeSpan.FromSeconds(8)) return (hit.Jpeg, "");
            bool busy = ctx.Scheduler.Running.Count > 0;
            var cands = StreamSelector.Order(ctx.Sources.Candidates(channelId), new Dictionary<long, int>())
                .Where(c => c.State != StreamState.Drm && !(busy && c.MaxConnections != null)).Take(2).ToList();
            if (cands.Count == 0) return (null, "nessun flusso utilizzabile per l'anteprima");
            string dir = Path.Combine(ctx.DataDir, "anteprime");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"{channelId}.jpg");
            foreach (var c in cands)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    var src = await ctx.Resolver.ResolveAsync(c.Url, c.UserAgent, c.Referrer, cts.Token);
                    var a = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-rw_timeout", "15000000" };
                    if (c.UserAgent.Length > 0) a.AddRange(["-user_agent", c.UserAgent]);
                    if (c.Referrer.Length > 0) a.AddRange(["-referer", c.Referrer]);
                    if (src.Format.Length > 0) a.AddRange(["-f", src.Format]);
                    a.AddRange(["-i", src.Url, "-an", "-sn", "-frames:v", "1", "-vf", "scale=480:-2", "-q:v", "5", "-f", "image2", file]);
                    File.Delete(file);
                    int code = await ctx.Tools.RunFfmpegAsync(a, Path.Combine(ctx.LogDir, "anteprima.log"), cts.Token);
                    if (code == 0 && File.Exists(file) && new FileInfo(file).Length > 0)
                    {
                        byte[] jpeg = await File.ReadAllBytesAsync(file, ct);
                        cache[channelId] = (ctx.Clock.GetUtcNow(), jpeg);
                        return (jpeg, "");
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException && !ct.IsCancellationRequested) { }
            }
            return (null, "il canale non risponde");
        }
        finally { gate.Release(); }
    }
}
