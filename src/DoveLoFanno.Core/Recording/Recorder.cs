using System.Globalization;
using DoveLoFanno.Media;
using DoveLoFanno.Sources;

namespace DoveLoFanno.Recording;

/// <summary>Cosa registrare: orari già comprensivi dei margini.</summary>
public sealed record RecordingJob(
    long Id, string Title, string Channel, DateTimeOffset Start, DateTimeOffset End, string Format,
    string OutDir, string BaseName, string LogDir, MediaInfo? Info = null, Func<DateTimeOffset>? EndNow = null)
{
    public bool AudioOnly => Format == "mp3";
    /// <summary>Fine in questo momento: si può spostare mentre registra (<see cref="EndNow"/>), altrimenti <see cref="End"/>.</summary>
    public DateTimeOffset CurrentEnd => EndNow?.Invoke() ?? End;
}

/// <summary>Parametri del registratore: quelli di RegistraTV, modificabili nei test.</summary>
public sealed record RecorderOptions
{
    /// <summary>File fermo per questo tempo = ffmpeg bloccato, si riavvia.</summary>
    public TimeSpan Stall { get; init; } = TimeSpan.FromSeconds(90);
    /// <summary>Ogni quanto si controllano file, fine e arresto.</summary>
    public TimeSpan Poll { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>Spezzone sotto questa dimensione = nessun dato ricevuto.</summary>
    public long MinPartBytes { get; init; } = 100 * 1024;
    /// <summary>Attesa massima tra due tentativi falliti (5 s, 10 s... fino a questa).</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Un salto dell'orologio di almeno questo tra due controlli = PC in sospensione.</summary>
    public TimeSpan SleepGap { get; init; } = TimeSpan.FromSeconds(60);
    /// <summary>Tentativi senza dati sullo stesso flusso prima di passare alla riserva.</summary>
    public int FailuresBeforeNextStream { get; init; } = 2;
    /// <summary>Si tengono gli spezzoni .ts dopo un'unione riuscita.</summary>
    public bool KeepTs { get; init; }
}

/// <summary>Esito: stato per la tabella recordings ('completed', 'incomplete', 'stopped', 'failed').</summary>
public sealed record RecordingOutcome(string State, string Output, bool MergeFailed, int LostMinutes, IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> LostRanges, IReadOnlyList<RecordedPart> Parts, string Log);

public sealed record RecordedPart(int No, long StreamId, string Url, string File, DateTimeOffset Started, DateTimeOffset Ended, string Reason);

/// <summary>Avvisi del registratore a chi lo usa (database, notifiche); tutti facoltativi.</summary>
public interface IRecordingObserver
{
    void Recording() { }
    void PartStarted(int no, long streamId, string url, string file, DateTimeOffset at) { }
    void PartEnded(RecordedPart part) { }
    void Merging() { }
}

/// <summary>
/// Tiene sveglio il PC durante le registrazioni (solo installazione su Windows). Hold e Release si chiamano una
/// volta per registrazione; con più registrazioni contemporanee il blocco resta finché non finisce l'ultima.
/// </summary>
public interface IKeepAwake
{
    void Hold();
    void Release();
}

public sealed class NoKeepAwake : IKeepAwake
{
    public static readonly NoKeepAwake Instance = new();
    public void Hold() { }
    public void Release() { }
}

/// <summary>
/// Registratore portato da Invoke-Recording di RegistraTV. ffmpeg copia il flusso senza ricodifica in spezzoni
/// MPEG-TS; si chiude all'orario di fine reale (-t è solo una sicurezza, +300 s: un HLS live parte con ~20 s di
/// arretrato letti di colpo); se il file non cresce per 90 s o ffmpeg esce prima della fine riparte in un nuovo
/// spezzone con attesa crescente fino a 30 s; un salto dell'orologio di 60 s o più è una sospensione del PC e la
/// parte persa si somma. Novità: dopo due tentativi senza dati sullo stesso flusso passa al flusso di riserva,
/// e ogni spezzone ricorda da quale flusso viene. Alla fine unisce in MP4 (o MP3) e scrive il .nfo.
/// </summary>
public sealed class Recorder(IMediaTools tools, StreamResolver resolver, TimeProvider clock, RecorderOptions? options = null, IKeepAwake? keepAwake = null)
{
    readonly RecorderOptions opt = options ?? new RecorderOptions();
    readonly IKeepAwake awake = keepAwake ?? NoKeepAwake.Instance;

    /// <param name="candidates">flussi del canale nell'ordine in cui provarli (almeno uno)</param>
    /// <param name="stop">arresto chiesto dall'utente: si chiude e si unisce quello che c'è</param>
    /// <param name="ct">chiusura del programma: si abbandona senza unire</param>
    /// <param name="resume">
    /// Ripresa dopo un riavvio del server (19/11/2026): gli spezzoni già registrati. Quelli ancora su disco entrano
    /// nell'unione, la numerazione continua dopo il numero più alto (prima ripartiva da 1 e sovrascriveva _part01.ts) e il
    /// tempo perso è solo la pausa tra la fine dell'ultimo spezzone e la ripresa.
    /// </param>
    public async Task<RecordingOutcome> RunAsync(RecordingJob job, IReadOnlyList<StreamCandidate> candidates, IRecordingObserver? observer = null,
        CancellationToken stop = default, CancellationToken ct = default, IReadOnlyList<RecordedPart>? resume = null)
    {
        if (candidates.Count == 0) throw new ArgumentException("nessun flusso da registrare", nameof(candidates));
        var obs = observer ?? new NullObserver();
        var run = new Run(this, job, ct);
        bool stopped = false;
        var parts = new List<RecordedPart>();
        int usedNo = resume is { Count: > 0 } ? resume.Max(x => x.No) : 0;
        if (resume != null) parts.AddRange(resume.Where(x => FileSize(x.File) >= opt.MinPartBytes).OrderBy(x => x.No));
        string state = "failed", output = "";
        bool mergeFailed = false;
        awake.Hold();
        try
        {
            run.Log($"Registrazione di {job.Channel}: dalle {Fmt(job.Start)} alle {Fmt(job.CurrentEnd)}");
            if (job.AudioOnly) run.Log("Solo audio: alla fine diventa un MP3");
            obs.Recording();

            // attesa dell'inizio
            run.LastTick = Now;
            while (Now < job.Start.AddSeconds(-5) && !stop.IsCancellationRequested)
            {
                if (!await run.Sleep(opt.Poll, stop)) break;
                run.CheckSleepGap();
            }
            stopped = stop.IsCancellationRequested;
            // ripresa dopo un riavvio: perso solo il tempo dalla fine dell'ultimo spezzone
            var late = Now - job.Start;
            if (!stopped && parts.Count > 0)
            {
                var from = parts[^1].Ended > job.Start ? parts[^1].Ended : job.Start;
                run.Log($"Registrazione ripresa: {parts.Count} {(parts.Count == 1 ? "spezzone già registrato" : "spezzoni già registrati")}");
                if ((Now - from).TotalSeconds > 60)
                {
                    var b = Min(Now, job.CurrentEnd);
                    run.AddLost(from, b);
                    run.Log($"Interruzione (server fermato o PC spento) dalle {Fmt(from)} alle {Fmt(b)}: persi {Minutes((b - from).TotalSeconds)}");
                }
            }
            // partenza in ritardo (PC spento o in sospensione): la parte mancante è persa
            else if (!stopped && late.TotalSeconds > 60)
            {
                var b = Min(Now, job.CurrentEnd);
                run.AddLost(job.Start, b);
                run.Log($"Registrazione partita in ritardo (PC spento o in sospensione?): persi {Minutes((b - job.Start).TotalSeconds)}");
            }

            Directory.CreateDirectory(job.OutDir);
            string basePath = Path.Combine(job.OutDir, job.BaseName);
            // alla ripresa i registri di ffmpeg continuano dopo quelli già scritti (_ffmpeg_01.log poteva essere ancora aperto)
            int failures = 0, streamFailures = 0, ci = 0, attempt = usedNo;
            var knownEnd = job.CurrentEnd;
            void EndChanged()
            {
                var e = job.CurrentEnd;
                if (e == knownEnd) return;
                run.Log($"Orario di fine cambiato: ora alle {Fmt(e)} (prima alle {Fmt(knownEnd)})");
                knownEnd = e;
            }
            while (!stopped)
            {
                EndChanged();
                int remaining = (int)Math.Ceiling((job.CurrentEnd - Now).TotalSeconds);
                if (remaining < 5) break;
                attempt++;
                var cand = candidates[ci];
                int no = Math.Max(usedNo, parts.Count == 0 ? 0 : parts.Max(x => x.No)) + 1;
                string part = $"{basePath}_part{no:D2}.ts";
                string ffLog = Path.Combine(job.LogDir, $"{job.Id}_ffmpeg_{attempt:D2}.log");

                var src = await resolver.ResolveAsync(cand.Url, cand.UserAgent, cand.Referrer, ct);
                if (src.Url != cand.Url) run.Log("Reindirizzato a: " + src.Url);
                if (src.Format.Length > 0) run.Log("Formato forzato: " + src.Format);
                var rate = RateOptions(src, tools.SupportsOption("readrate_catchup"));
                if (rate.Count > 0) run.Log("DASH a velocità reale: " + string.Join(' ', rate));
                var started = Now;
                obs.PartStarted(no, cand.StreamId, src.Url, part, started);
                run.Log($"Avvio ffmpeg, spezzone {no}, flusso {cand.StreamId}, {remaining} s rimanenti");
                string reason = "exit";
                using (var p = tools.StartFfmpeg(Args(job, cand, src, rate, part, remaining), ffLog))
                {
                    long lastSize = -1;
                    var lastGrow = Now;
                    run.LastTick = Now;
                    while (!p.HasExited)
                    {
                        if (!await run.Sleep(opt.Poll, stop))
                        {
                            run.Log("Arresto richiesto dall'utente");
                            stopped = true; reason = "stop"; p.Kill(); break;
                        }
                        // dopo una sospensione ffmpeg può riprendersi da solo: gli si dà Stall dal risveglio
                        if (run.CheckSleepGap()) lastGrow = Now;
                        long size = FileSize(part);
                        if (size > lastSize) { lastSize = size; lastGrow = Now; }
                        else if (Now - lastGrow > opt.Stall)
                        {
                            run.Log($"Il file non cresce da {opt.Stall.TotalSeconds:0} s: riavvio ffmpeg");
                            reason = "stall"; p.Kill(); break;
                        }
                        EndChanged();
                        if (Now >= job.CurrentEnd)
                        {
                            run.Log("Orario di fine raggiunto: chiudo ffmpeg");
                            reason = "end"; p.Kill(); break;
                        }
                    }
                    await p.WaitForExitAsync(TimeSpan.FromSeconds(15));
                }

                long finalSize = FileSize(part);
                TimeSpan wait;
                if (finalSize < opt.MinPartBytes)
                {
                    TryDelete(part);
                    failures++;
                    streamFailures++;
                    wait = TimeSpan.FromSeconds(Math.Min(opt.MaxBackoff.TotalSeconds, 5 * failures));
                    run.Log($"Nessun dato ricevuto, nuovo tentativo tra {wait.TotalSeconds:0} s (dettagli in {ffLog})");
                    if (streamFailures >= opt.FailuresBeforeNextStream && candidates.Count > 1)
                    {
                        ci = (ci + 1) % candidates.Count;
                        streamFailures = 0;
                        run.Log($"Passo al flusso {candidates[ci].StreamId}");
                    }
                }
                else
                {
                    var rp = new RecordedPart(no, cand.StreamId, src.Url, part, started, Now, reason);
                    parts.Add(rp);
                    obs.PartEnded(rp);
                    failures = 0;
                    streamFailures = 0;
                    wait = TimeSpan.FromSeconds(2);
                    run.Log($"Spezzone {no} chiuso: {finalSize / 1048576.0:0.0} MB");
                    if (!stopped && Now < job.CurrentEnd.AddSeconds(-5)) run.Log("Il flusso si è interrotto prima della fine: riprendo la registrazione");
                }
                if (!stopped && Now < job.CurrentEnd.AddSeconds(-5) && !await run.Sleep(wait, stop)) stopped = true;
            }

            if (parts.Count == 0)
            {
                state = stopped ? "stopped" : "failed";
                run.Log("Nessun dato registrato");
            }
            else
            {
                obs.Merging();
                string? merged = await Merger.MergeAsync(tools, parts.Select(x => x.File).ToList(), basePath, job, run.Log, ct);
                if (merged != null)
                {
                    if (!opt.KeepTs) foreach (var x in parts) TryDelete(x.File);
                    output = merged;
                    state = "completed";
                    run.Log("File finale: " + merged);
                    if (job.Info != null && !job.AudioOnly) Nfo.Write(job.Info, Path.ChangeExtension(merged, ".nfo"));
                }
                else
                {
                    output = parts[0].File;
                    mergeFailed = true;
                    state = "completed";
                    run.Log("Unione non riuscita: restano i file .ts, che si aprono con un normale lettore video");
                }
                if (stopped) state = "stopped";
                else if (run.LostSeconds >= 60)
                {
                    state = "incomplete";
                    run.Log($"Registrazione incompleta: mancano {Minutes(run.LostSeconds)} ({string.Join(", ", run.Lost.Select(r => Fmt(r.From) + "-" + Fmt(r.To)))})");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            state = "failed";
            run.Log("ERRORE: " + ex.Message);
        }
        finally
        {
            awake.Release();
        }
        int lostMin = run.LostSeconds >= 60 ? (int)Math.Round(run.LostSeconds / 60) : 0;
        return new RecordingOutcome(state, output, mergeFailed, lostMin, run.Lost, parts, run.LogPath);
    }

    DateTimeOffset Now => clock.GetLocalNow();
    TimeProvider Clock => clock;

    static List<string> Args(RecordingJob job, StreamCandidate c, ResolvedStream src, IReadOnlyList<string> rate, string part, int remaining)
    {
        var a = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "warning", "-y", "-rw_timeout", "30000000" };
        if (c.UserAgent.Length > 0) a.AddRange(["-user_agent", c.UserAgent]);
        if (c.Referrer.Length > 0) a.AddRange(["-referer", c.Referrer]);
        if (src.Format.Length > 0) a.AddRange(["-f", src.Format]);
        a.AddRange(rate);
        a.AddRange(["-i", src.Url]);
        // solo audio: senza flush ffmpeg scrive a blocchi da 256 KB, cioè ogni 15-30 s a 64-128 kbit/s; il controllo
        // "file fermo" resterebbe cieco e un'interruzione perderebbe l'ultimo blocco (trovato nel collaudo del 16/11)
        if (job.AudioOnly) a.AddRange(["-map", "0:a:0", "-vn", "-flush_packets", "1"]);
        a.AddRange(["-sn", "-dn", "-c", "copy", "-t", (remaining + 300).ToString(CultureInfo.InvariantCulture), "-f", "mpegts", part]);
        return a;
    }

    /// <summary>
    /// DASH a velocità reale, come RegistraTV dal 19/11/2026. Letto alla massima velocità il lettore DASH di ffmpeg
    /// arriva al bordo della diretta e chiede frammenti non ancora pubblicati: 404 a raffica, audio con buchi da 3,84 s
    /// («C'è Posta per Te», La 5, 18/11) oppure ffmpeg fermo finché il controllo non lo riavvia ogni 97 s («Amabili
    /// resti», LA7 Cinema, 25/11: 96 spezzoni, 54 min su 157). -readrate_catchup lascia recuperare un ritardo a 1,5x
    /// (il server Mediaset tiene solo 2 min di diretta); senza (ffmpeg prima del 7.1) resta solo -readrate 1.
    /// </summary>
    public static IReadOnlyList<string> RateOptions(ResolvedStream src, bool catchupSupported)
    {
        if (!IsDash(src)) return [];
        return catchupSupported ? ["-readrate", "1", "-readrate_catchup", "1.5"] : ["-readrate", "1"];
    }

    static bool IsDash(ResolvedStream src)
    {
        if (src.Format.Equals("dash", StringComparison.OrdinalIgnoreCase)) return true;
        string path = Uri.TryCreate(src.Url, UriKind.Absolute, out var u) ? u.AbsolutePath : src.Url.Split('?')[0];
        return path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase);
    }

    static long FileSize(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch (IOException) { return 0; }
    }

    internal static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
    // ora locale del server: gli orari del database sono in UTC e nel registro uscivano due ore indietro d'estate (una d'inverno)
    static string Fmt(DateTimeOffset d) => TimeZoneInfo.ConvertTime(d, TimeZoneInfo.Local).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

    internal static string Minutes(double sec)
    {
        int m = (int)Math.Round(sec / 60);
        if (m < 60) return $"{m} min";
        return m % 60 == 0 ? $"{m / 60} h" : $"{m / 60} h {m % 60} min";
    }

    sealed class NullObserver : IRecordingObserver { }

    /// <summary>Stato di una registrazione in corso: log, sospensioni, minuti persi.</summary>
    sealed class Run
    {
        readonly Recorder r;
        readonly RecordingJob job;
        readonly CancellationToken ct;
        public readonly string LogPath;
        public DateTimeOffset? LastTick;
        public double LostSeconds;
        public readonly List<(DateTimeOffset From, DateTimeOffset To)> Lost = [];

        public Run(Recorder r, RecordingJob job, CancellationToken ct)
        {
            this.r = r;
            this.job = job;
            this.ct = ct;
            Directory.CreateDirectory(job.LogDir);
            LogPath = Path.Combine(job.LogDir, job.Id.ToString(CultureInfo.InvariantCulture) + ".log");
        }

        public void Log(string msg)
        {
            string line = r.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + msg + Environment.NewLine;
            try { File.AppendAllText(LogPath, line); } catch (IOException) { }
        }

        /// <summary>Attende; false se nel frattempo è arrivato l'arresto. La chiusura del programma lancia.</summary>
        public async Task<bool> Sleep(TimeSpan d, CancellationToken stop)
        {
            using var both = CancellationTokenSource.CreateLinkedTokenSource(stop, ct);
            try { await Task.Delay(d, r.Clock, both.Token); return true; }
            catch (OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                return false;
            }
        }

        public void AddLost(DateTimeOffset a, DateTimeOffset b)
        {
            if (b <= a) return;
            LostSeconds += (b - a).TotalSeconds;
            Lost.Add((a, b));
        }

        /// <summary>Vero se dall'ultimo controllo l'orologio è saltato avanti (PC in sospensione).</summary>
        public bool CheckSleepGap()
        {
            var now = r.Now;
            var prev = LastTick;
            LastTick = now;
            if (prev is not DateTimeOffset p || now - p < r.opt.SleepGap) return false;
            Log($"Il PC è stato in sospensione dalle {Fmt(p)} alle {Fmt(now)}");
            var a = Max(p, job.Start);
            var b = Min(now, job.CurrentEnd);
            if (b > a)
            {
                AddLost(a, b);
                Log($"Persi {Minutes((b - a).TotalSeconds)} di registrazione");
            }
            return true;
        }
    }
}
