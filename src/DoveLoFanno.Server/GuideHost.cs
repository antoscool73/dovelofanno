using DoveLoFanno.Data;
using DoveLoFanno.Epg;

namespace DoveLoFanno.Server;

/// <summary>
/// Guide TV: ogni 10 minuti controlla le fonti XMLTV (quelle delle playlist e quelle aggiunte a mano) e riscarica quelle
/// caricate più di 12 ore fa. Come RegistraTV la copia salvata (data/epg/&lt;id&gt;.xml.gz) si sostituisce solo se il
/// file nuovo si legge; se lo scaricamento non riesce resta la guida di prima, con l'errore annotato.
/// </summary>
public sealed class GuideHost(ServerContext ctx, RatingsHost ratings, ILogger<GuideHost> log) : BackgroundService
{
    static readonly TimeSpan MaxAge = TimeSpan.FromHours(12);
    readonly SemaphoreSlim wake = new(0);
    volatile bool force;

    /// <summary>Riscarica subito tutte le guide (pulsante "Aggiorna guida").</summary>
    public void RefreshNow()
    {
        force = true;
        wake.Release();
    }

    /// <summary>Una playlist è cambiata: forse indica una guida nuova.</summary>
    public void Kick() => wake.Release();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        string dir = Path.Combine(ctx.DataDir, "epg");
        Directory.CreateDirectory(dir);
        bool startup = true;
        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(dir, startup, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Errore nel caricamento della guida"); }
            startup = false;
            try { await wake.WaitAsync(TimeSpan.FromMinutes(10), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    async Task RunOnceAsync(string dir, bool startup, CancellationToken ct)
    {
        ctx.Guide.SyncFromPlaylists();
        bool all = force;
        force = false;
        var now = ctx.Clock.GetUtcNow();
        foreach (var g in ctx.Guide.Sources().Where(x => x.Enabled))
        {
            string file = Path.Combine(dir, g.Id + ".xml.gz");
            bool stale = all || g.LastLoadedAt is not DateTimeOffset t || now - t > MaxAge || g.Programmes == 0;
            if (!stale) continue;
            string tmp = file + ".tmp";
            try
            {
                await DownloadAsync(g.Url, tmp, ct);
                var guide = XmltvReader.Load(tmp, ctx.Clock.GetLocalNow().DateTime);
                File.Move(tmp, file, overwrite: true);
                int n = ctx.Guide.Save(g.Id, guide, ctx.Clock.GetUtcNow());
                log.LogInformation("Guida {Url}: {Channels} canali, {Programmes} programmi", g.Url, guide.ChannelCount, n);
                ratings.Kick();   // film nuovi: si cercano i voti
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or System.Xml.XmlException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                try { File.Delete(tmp); } catch (IOException) { }
                string msg = "guida non aggiornata: " + ex.Message;
                // all'avvio, con il database vuoto, si riparte dalla copia salvata
                if (File.Exists(file) && (startup || g.Programmes == 0))
                {
                    try
                    {
                        ctx.Guide.Save(g.Id, XmltvReader.Load(file, ctx.Clock.GetLocalNow().DateTime), ctx.Clock.GetUtcNow());
                        msg += " (si usa la copia salvata)";
                    }
                    catch (Exception inner) when (inner is IOException or InvalidDataException or System.Xml.XmlException) { }
                }
                ctx.Guide.SetError(g.Id, msg, ctx.Clock.GetUtcNow());
                log.LogWarning("Guida {Url}: {Error}", g.Url, msg);
            }
        }
    }

    async Task DownloadAsync(string url, string path, CancellationToken ct)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(url, path, overwrite: true);   // file sul server
            return;
        }
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(2));
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("DoveLoFanno");
        using var resp = await ctx.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        resp.EnsureSuccessStatusCode();
        await using var f = File.Create(path);
        await resp.Content.CopyToAsync(f, cts.Token);
    }
}
