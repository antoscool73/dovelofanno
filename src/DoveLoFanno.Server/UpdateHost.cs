using System.Globalization;
using System.Text.Json;
using DoveLoFanno.Updates;

namespace DoveLoFanno.Server;

public sealed record UpdateStatus(bool Enabled, string Current, string? Latest, bool UpdateAvailable, string? Date, string? Notes,
    string? Url, DateTimeOffset? CheckedAt, string Error);
public sealed record UpdateSettingsPatch(bool? Enabled);

/// <summary>
/// Controllo degli aggiornamenti (19/11/2026): 2 minuti dopo l'avvio e poi una volta al giorno legge version.json dal
/// sito (o dall'indirizzo in DoveLoFanno:UpdateFeed, anche un file locale per i collaudi). Se c'è una versione più nuova
/// manda una notifica ai profili adulti, una volta sola per versione. Si spegne in Impostazioni › Aggiornamenti; la
/// richiesta non porta con sé dati dell'utente, solo la versione nel nome del programma (User-Agent).
/// </summary>
public sealed class UpdateHost(ServerContext ctx, IConfiguration config, ILogger<UpdateHost> log) : BackgroundService
{
    public const string DefaultFeed = "https://dovelofanno.it/version.json";
    static readonly TimeSpan Every = TimeSpan.FromDays(1);
    readonly SemaphoreSlim gate = new(1, 1);
    string error = "";

    string Feed => config["DoveLoFanno:UpdateFeed"] is { Length: > 0 } f ? f : DefaultFeed;
    public bool Enabled => ctx.Settings.Get("UpdateCheck") != "0";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), ctx.Clock, ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            if (Enabled && (CheckedAt() is not { } last || ctx.Clock.GetUtcNow() - last >= Every))
            {
                try { await CheckAsync(ct); }
                catch (OperationCanceledException) { break; }
            }
            try { await Task.Delay(TimeSpan.FromHours(1), ctx.Clock, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void SetEnabled(bool on) => ctx.Settings.Set("UpdateCheck", on ? "1" : "0");

    /// <summary>Controlla subito (anche con il controllo automatico spento: l'ha chiesto qualcuno).</summary>
    public async Task<UpdateStatus> CheckAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var now = ctx.Clock.GetUtcNow();
            try
            {
                string json = await ReadFeedAsync(ct);
                var r = Releases.Parse(json);
                ctx.Settings.Set("UpdateLatest", JsonSerializer.Serialize(r));
                error = "";
                if (Releases.IsNewer(r.Version, Releases.Current) && ctx.Settings.Get("UpdateNotified") != r.Version)
                {
                    ctx.Settings.Set("UpdateNotified", r.Version);
                    string text = r.Notes.Length > 0 ? r.Notes : $"Ora hai la {Releases.Current}.";
                    foreach (var p in ctx.Profiles.List().Where(p => !p.IsChild))
                        ctx.Notifications.Add(p.Id, "system", $"È uscita la versione {r.Version}", text, "/impostazioni#aggiornamenti", now);
                    log.LogInformation("Aggiornamento disponibile: {Latest} (installata {Current})", r.Version, Releases.Current);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or FormatException or UnauthorizedAccessException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                error = ex switch
                {
                    TaskCanceledException => "il sito non ha risposto in tempo",
                    HttpRequestException { StatusCode: null } => "sito degli aggiornamenti non raggiungibile",
                    HttpRequestException h => h.Message,
                    JsonException or FormatException => "il sito ha risposto, ma con dati che non si capiscono",
                    _ => ex.Message,
                };
                log.LogWarning("Controllo degli aggiornamenti non riuscito: {Error}", error);
            }
            // anche un controllo non riuscito conta: si riprova domani, non ogni ora
            ctx.Settings.Set("UpdateCheckedAt", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
            return Status();
        }
        finally { gate.Release(); }
    }

    async Task<string> ReadFeedAsync(CancellationToken ct)
    {
        string feed = Feed;
        if (!feed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !feed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return await File.ReadAllTextAsync(feed, ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        using var req = new HttpRequestMessage(HttpMethod.Get, feed);
        req.Headers.UserAgent.Clear();
        req.Headers.UserAgent.ParseAdd($"DoveLoFanno/{Releases.Current}");
        req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var res = await ctx.Http.SendAsync(req, cts.Token);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"il sito ha risposto {(int)res.StatusCode}", null, res.StatusCode);
        return await res.Content.ReadAsStringAsync(cts.Token);
    }

    DateTimeOffset? CheckedAt() =>
        long.TryParse(ctx.Settings.Get("UpdateCheckedAt"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long s) ? DateTimeOffset.FromUnixTimeSeconds(s) : null;

    public UpdateStatus Status()
    {
        ReleaseInfo? r = null;
        try { if (ctx.Settings.Get("UpdateLatest") is { Length: > 0 } j) r = JsonSerializer.Deserialize<ReleaseInfo>(j); }
        catch (JsonException) { }
        return new UpdateStatus(Enabled, Releases.Current, r?.Version, r != null && Releases.IsNewer(r.Version, Releases.Current),
            r?.Date is { Length: > 0 } d ? d : null, r?.Notes, r?.Url, CheckedAt(), error);
    }
}
