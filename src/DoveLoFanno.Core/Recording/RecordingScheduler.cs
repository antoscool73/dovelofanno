using System.Collections.Concurrent;
using DoveLoFanno.Data;
using DoveLoFanno.Sources;

namespace DoveLoFanno.Recording;

/// <summary>Cartelle e tempi del programmatore.</summary>
public sealed record SchedulerOptions(string RecordingsDir, string LogDir)
{
    /// <summary>Quanto prima dell'inizio (margine compreso) si sceglie il flusso e si avvia il registratore.</summary>
    public TimeSpan Lead { get; init; } = TimeSpan.FromMinutes(2);
    /// <summary>Una copia della playlist più vecchia di così si rilegge prima di registrare.</summary>
    public TimeSpan PlaylistMaxAge { get; init; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Avvia le registrazioni in programma e le segue fino alla fine. Il server chiama <see cref="TickAsync"/> ogni
/// pochi secondi. La registrazione memorizza il canale: il flusso si sceglie solo al momento di registrare, dopo
/// aver riletto la playlist, così i token che scadono e le playlist cambiate non rompono quelle programmate.
/// </summary>
public sealed class RecordingScheduler(
    RecordingStore recordings, SourceStore sources, Recorder recorder, HttpClient http, TimeProvider clock, SchedulerOptions options)
{
    sealed class Active
    {
        public required Task Task;
        public required CancellationTokenSource Stop;
        public long CurrentSource;
        /// <summary>Fine della registrazione (margine compreso) in tick UTC: si sposta con <see cref="ChangeEnd"/>.</summary>
        public long EndTicks;
        public DateTimeOffset End => new(Interlocked.Read(ref EndTicks), TimeSpan.Zero);
    }

    readonly ConcurrentDictionary<long, Active> running = new();
    readonly CancellationTokenSource shutdown = new();

    /// <summary>Registrazione finita (per le notifiche): id ed esito.</summary>
    public event Action<long, RecordingOutcome>? Finished;

    public IReadOnlyCollection<long> Running => running.Keys.ToList();

    /// <summary>Da chiamare all'avvio, prima del primo giro.</summary>
    public int Recover() => recordings.RecoverAfterRestart(clock.GetUtcNow());

    public Task TickAsync()
    {
        var now = clock.GetUtcNow();
        foreach (var r in recordings.Due(now, options.Lead))
        {
            if (running.ContainsKey(r.Id)) continue;
            var stop = new CancellationTokenSource();
            var a = new Active { Task = Task.CompletedTask, Stop = stop, EndTicks = r.RecordTo.UtcTicks };
            if (!running.TryAdd(r.Id, a)) continue;
            recordings.SetState(r.Id, "recording");
            a.Task = Task.Run(() => RunOneAsync(r, a));
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sposta la fine di una registrazione in programma o in corso (anche durante: il registratore la rilegge a ogni
    /// controllo). false se non è in programma né in corso.
    /// </summary>
    public bool ChangeEnd(long id, DateTimeOffset stop, int afterMin)
    {
        if (!recordings.ChangeEnd(id, stop, afterMin)) return false;
        if (running.TryGetValue(id, out var a)) Interlocked.Exchange(ref a.EndTicks, stop.AddMinutes(afterMin).UtcTicks);
        return true;
    }

    /// <summary>Ferma una registrazione in corso (si unisce quello che c'è) o annulla una in programma.</summary>
    public bool Stop(long id)
    {
        if (running.TryGetValue(id, out var a)) { a.Stop.Cancel(); return true; }
        return recordings.Cancel(id);
    }

    /// <summary>Chiusura del server: abbandona le registrazioni in corso (ripartiranno al riavvio).</summary>
    public async Task ShutdownAsync()
    {
        shutdown.Cancel();
        try { await Task.WhenAll(running.Values.Select(a => a.Task)).WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException) { }
    }

    async Task RunOneAsync(RecordingRow r, Active a)
    {
        try
        {
            var candidates = await CandidatesAsync(r.ChannelId);
            if (candidates.Count == 0)
            {
                recordings.Fail(r.Id, "nessun flusso disponibile per il canale (DRM, sorgenti disattivate o tutte le connessioni occupate)");
                return;
            }
            a.CurrentSource = candidates[0].SourceId;
            // cartelle e nomi che Jellyfin, Plex ed Emby riconoscono, con il .nfo per film ed episodi
            var (folder, baseName, info) = LibraryLayout.For(r.Title, r.ChannelName, r.Start, r.Format, recordings.Media(r.Id), options.RecordingsDir);
            var job = new RecordingJob(r.Id, r.Title, r.ChannelName, r.RecordFrom, r.RecordTo, r.Format,
                Path.Combine(options.RecordingsDir, folder), baseName, options.LogDir, info, () => a.End);
            // dopo un riavvio del server si continua dagli spezzoni già registrati (quello interrotto non ha l'ora di fine:
            // vale l'ultima modifica del file)
            var resume = recordings.PartsSoFar(r.Id).Select(p => new RecordedPart(p.PartNo, p.StreamId ?? 0, p.Url, p.File, p.StartedAt,
                p.EndedAt ?? (File.Exists(p.File) ? new DateTimeOffset(File.GetLastWriteTimeUtc(p.File), TimeSpan.Zero) : p.StartedAt),
                p.EndedAt == null ? "restart" : p.ExitReason)).ToList();
            var outcome = await recorder.RunAsync(job, candidates, new Observer(recordings, r.Id, candidates, a), a.Stop.Token, shutdown.Token, resume);
            recordings.Finish(r.Id, outcome);
            Finished?.Invoke(r.Id, outcome);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            // resta "recording": RecoverAfterRestart la fa ripartire
        }
        catch (Exception ex)
        {
            recordings.Fail(r.Id, "errore: " + ex.Message);
        }
        finally
        {
            running.TryRemove(r.Id, out _);
            a.Stop.Dispose();
        }
    }

    /// <summary>Rilegge le playlist vecchie che contengono il canale, poi ordina i flussi.</summary>
    async Task<List<StreamCandidate>> CandidatesAsync(long channelId)
    {
        var now = clock.GetUtcNow();
        var involved = sources.Candidates(channelId).Select(c => c.SourceId).Distinct().ToHashSet();
        foreach (var s in sources.Sources().Where(s => s.Enabled && involved.Contains(s.Id)))
        {
            if (s.LastCheckedAt is DateTimeOffset t && now - t < options.PlaylistMaxAge) continue;
            try { sources.Import(s.Id, M3uParser.Parse(await M3uParser.LoadTextAsync(s.Url, http, shutdown.Token)), now); }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                // si usa la copia che c'è
                sources.SetSourceError(s.Id, "playlist non raggiungibile: " + ex.Message, now);
            }
        }
        var inUse = running.Values.Where(x => x.CurrentSource != 0).GroupBy(x => x.CurrentSource).ToDictionary(g => g.Key, g => g.Count());
        return StreamSelector.Order(sources.Candidates(channelId), inUse);
    }

    sealed class Observer(RecordingStore store, long id, List<StreamCandidate> candidates, Active a) : IRecordingObserver
    {
        public void PartStarted(int no, long streamId, string url, string file, DateTimeOffset at)
        {
            a.CurrentSource = candidates.FirstOrDefault(c => c.StreamId == streamId)?.SourceId ?? a.CurrentSource;
            store.PartStarted(id, no, streamId, url, file, at);
        }

        public void PartEnded(RecordedPart part) => store.PartEnded(id, part);
        public void Merging() => store.SetState(id, "merging");
    }
}
