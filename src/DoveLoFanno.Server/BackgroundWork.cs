using System.Threading.Channels;

namespace DoveLoFanno.Server;

/// <summary>Giro del programmatore delle registrazioni ogni 5 secondi.</summary>
public sealed class SchedulerHost(ServerContext ctx, ILogger<SchedulerHost> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        int again = ctx.Scheduler.Recover();
        if (again > 0) log.LogInformation("{Count} registrazioni interrotte ripartono", again);
        ctx.Scheduler.Finished += (id, o) => log.LogInformation("Registrazione {Id}: {State} {File}", id, o.State, o.Output);
        while (!ct.IsCancellationRequested)
        {
            try { await ctx.Scheduler.TickAsync(); }
            catch (Exception ex) { log.LogError(ex, "Errore del programmatore"); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), ctx.Clock, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await ctx.Scheduler.ShutdownAsync();
        await base.StopAsync(cancellationToken);
    }
}

/// <summary>
/// Controllo dei flussi con ffprobe, uno alla volta per non saturare le sorgenti: alla prima aggiunta di una
/// sorgente, a richiesta, e ogni notte alle 4 per tutte.
/// </summary>
public sealed class ProbeHost(ServerContext ctx, ILogger<ProbeHost> log) : BackgroundService
{
    readonly Channel<long?> queue = Channel.CreateUnbounded<long?>();

    /// <summary>Mette in coda il controllo di una sorgente (null = tutte).</summary>
    public void Enqueue(long? sourceId) => queue.Writer.TryWrite(sourceId);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _ = NightlyAsync(ct);
        await foreach (long? sourceId in queue.Reader.ReadAllAsync(ct))
        {
            var streams = ctx.Sources.Streams(sourceId: sourceId).Where(s => s.State != Sources.StreamState.Drm).ToList();
            log.LogInformation("Controllo di {Count} flussi", streams.Count);
            foreach (var s in streams)
            {
                // mai durante le registrazioni dalla stessa sorgente: occuperebbe una connessione
                if (ctx.Scheduler.Running.Count > 0 && ctx.Sources.Source(s.SourceId)?.MaxConnections is not null) continue;
                try
                {
                    string ua = s.UserAgent;
                    var cand = ctx.Sources.Candidates(s.ChannelId).FirstOrDefault(c => c.StreamId == s.Id);
                    var p = await ctx.Prober.ProbeAsync(s.Url, cand?.UserAgent ?? ua, cand?.Referrer ?? "", ct);
                    ctx.Sources.SaveProbe(s.Id, p, ctx.Clock.GetUtcNow());
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { log.LogWarning("Controllo del flusso {Id} non riuscito: {Error}", s.Id, ex.Message); }
            }
        }
    }

    async Task NightlyAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = ctx.Clock.GetLocalNow();
            var next = new DateTimeOffset(now.Date.AddHours(4), now.Offset);
            if (next <= now) next = next.AddDays(1);
            try { await Task.Delay(next - now, ctx.Clock, ct); }
            catch (OperationCanceledException) { return; }
            Enqueue(null);
        }
    }
}
