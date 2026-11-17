using DoveLoFanno.Library;
using DoveLoFanno.Ratings;

namespace DoveLoFanno.Server;

/// <summary>
/// Libreria in background: scansione all'avvio, ogni notte alle 5, a richiesta e (solo le registrazioni) a fine
/// registrazione; copie per il browser una alla volta, solo per i file che qualcuno vuole guardare.
/// </summary>
public sealed class LibraryHost(ServerContext ctx, ILogger<LibraryHost> log) : BackgroundService
{
    readonly SemaphoreSlim scanWake = new(0);
    readonly SemaphoreSlim copyWake = new(0);
    volatile bool fullScanRequested = true;     // la prima volta all'avvio
    volatile bool recordingsChanged;

    public LibraryScanner Scanner { get; } = new(ctx.Library, ctx.Works, ctx.Tools, ctx.Clock);
    public bool Scanning { get; private set; }
    public ScanReport? LastReport { get; private set; }
    public DateTimeOffset? LastScanAt { get; private set; }
    public string CopyStatus { get; private set; } = "";

    /// <summary>Rilegge tutte le cartelle appena possibile.</summary>
    public void ScanNow()
    {
        fullScanRequested = true;
        scanWake.Release();
    }

    /// <summary>Una registrazione è finita: entra nella libreria senza rileggere le cartelle.</summary>
    public void RecordingFinished()
    {
        recordingsChanged = true;
        scanWake.Release();
    }

    public void CopyQueued() => copyWake.Release();

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        ctx.Scheduler.Finished += (_, _) => RecordingFinished();
        ctx.Library.RequeueInterruptedCopies();
        return Task.WhenAll(ScanLoopAsync(ct), CopyLoopAsync(ct), Daily.RunAsync(ctx.Clock, 5, ScanNow, ct));
    }

    ILibraryTmdb? Tmdb()
    {
        string key = ctx.Settings.Get("TmdbKey");
        return key.Length > 0 ? new TmdbClient(ctx.Http, key) : null;
    }

    async Task ScanLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (fullScanRequested)
                {
                    fullScanRequested = false;
                    recordingsChanged = false;
                    Scanning = true;
                    var r = await Scanner.ScanAsync(Tmdb(), ct);
                    ImdbRatingsForLibrary();
                    LastReport = r;
                    LastScanAt = ctx.Clock.GetUtcNow();
                    log.LogInformation("Libreria: {Files} file in {Folders} cartelle, {New} nuovi, {Missing} mancanti, {Matched} riconosciuti, {Unmatched} da sistemare, {Recordings} registrazioni {Error}",
                        r.Files, r.Folders, r.New, r.Missing, r.Matched, r.Unmatched, r.Recordings, r.Error);
                }
                else if (recordingsChanged)
                {
                    recordingsChanged = false;
                    int n = await Scanner.SyncRecordingsAsync(ct);
                    if (n > 0) log.LogInformation("Libreria: {Count} registrazioni aggiunte", n);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Errore nella scansione della libreria"); }
            finally { Scanning = false; }
            try { await scanWake.WaitAsync(ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Voti IMDb anche per le serie e i film della libreria che non passano in guida, dal file di IMDb che il servizio dei
    /// voti ha già scaricato (solo se i voti IMDb sono attivi: la licenza del file è personale).
    /// </summary>
    void ImdbRatingsForLibrary()
    {
        string file = Path.Combine(ctx.DataDir, "ratings", "title.ratings.tsv.gz");
        if (ctx.Settings.Get("ImdbRatings") != "1" || !File.Exists(file)) return;
        var need = ctx.Library.WorksWithoutImdbRating();
        if (need.Count == 0) return;
        var found = ImdbRatings.Read(file, need.Select(x => x.ImdbId).ToHashSet());
        int n = 0;
        foreach (var (work, imdb) in need)
            if (found.TryGetValue(imdb, out var r) && r.Votes > 0) { ctx.Library.SetImdbRating(work, r.Rating, r.Votes); n++; }
        if (n > 0) log.LogInformation("Libreria: voto IMDb per {Count} opere", n);
    }

    async Task CopyLoopAsync(CancellationToken ct)
    {
        string dir = Path.Combine(ctx.DataDir, "browser");
        while (!ct.IsCancellationRequested)
        {
            while (ctx.Library.NextCopy() is { } next)
            {
                var (id, path) = next;
                var probe = ctx.Library.StoredProbe(id);
                if (probe == null) { ctx.Library.SetCopy(id, "failed"); continue; }
                Directory.CreateDirectory(dir);
                string tmp = Path.Combine(dir, id + ".tmp.mp4"), done = Path.Combine(dir, id + ".mp4");
                var how = MediaProbe.Decide(path, probe);
                ctx.Library.SetCopy(id, "working");
                CopyStatus = how switch { Playback.Full => "Ricodifica completa", Playback.Audio => "Ricodifica dell'audio", _ => "Copia" } + " di " + Path.GetFileName(path);
                log.LogInformation("Copia per il browser ({How}) di {File}", how, path);
                int code;
                try { code = await ctx.Tools.RunFfmpegAsync(MediaProbe.CopyArgs(path, tmp, probe, how), Path.Combine(ctx.LogDir, $"copia-{id}.log"), ct); }
                catch (OperationCanceledException) { TryDelete(tmp); ctx.Library.SetCopy(id, "queued"); return; }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { code = -1; log.LogWarning("Copia non riuscita: {Error}", ex.Message); }
                if (code == 0 && File.Exists(tmp))
                {
                    File.Move(tmp, done, true);
                    ctx.Library.SetCopy(id, "done", done);
                }
                else
                {
                    TryDelete(tmp);
                    ctx.Library.SetCopy(id, "failed");
                    log.LogWarning("Copia per il browser di {File} non riuscita (ffmpeg {Code})", path, code);
                }
                CopyStatus = "";
            }
            try { await copyWake.WaitAsync(TimeSpan.FromMinutes(1), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
