using DoveLoFanno.Media;
using DoveLoFanno.Recording;
using DoveLoFanno.Sources;
using Xunit.Abstractions;

namespace DoveLoFanno.Tests;

[CollectionDefinition("ffmpeg", DisableParallelization = true)]
public sealed class FfmpegCollection;

/// <summary>
/// Collaudo del registratore con ffmpeg vero su un HLS live simulato: le stesse prove fatte su RegistraTV
/// (reindirizzamento, estensione non standard, rete che cade e ripresa, solo audio, arresto, sospensione),
/// più il passaggio al flusso di riserva. Senza ffmpeg i test non controllano nulla (in CI ffmpeg è obbligatorio).
/// </summary>
[Collection("ffmpeg")]
public sealed class RecorderTests(ITestOutputHelper output) : IAsyncLifetime
{
    readonly string work = Path.Combine(Path.GetTempPath(), "dlf-rec-" + Guid.NewGuid().ToString("N"));
    HlsSimulator? sim;
    MediaTools? tools;

    static readonly RecorderOptions Fast = new()
    {
        Poll = TimeSpan.FromSeconds(1),
        Stall = TimeSpan.FromSeconds(6),
        MaxBackoff = TimeSpan.FromSeconds(1),
        MinPartBytes = 50 * 1024,
    };

    public async Task InitializeAsync()
    {
        tools = HlsSimulator.FindTools();
        if (tools != null) sim = await HlsSimulator.StartAsync(tools);
    }

    public async Task DisposeAsync()
    {
        if (sim != null) await sim.DisposeAsync();
        try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    bool Ready()
    {
        if (sim != null) return true;
        output.WriteLine("ffmpeg non trovato: test saltato");
        return false;
    }

    StreamCandidate Cand(long id, string path) => new(id, 1, sim!.BaseUrl + path, "DoveLoFanno-test", "", StreamState.Ok, 0, 0, null);

    RecordingJob Job(int seconds, string format = "mp4", MediaInfo? info = null, TimeProvider? clock = null, long id = 1)
    {
        var now = (clock ?? TimeProvider.System).GetLocalNow();
        return new RecordingJob(id, "Prova", "Canale di prova", now, now.AddSeconds(seconds), format, work, "Prova " + id, Path.Combine(work, "logs"), info);
    }

    Recorder NewRecorder(TimeProvider? clock = null) =>
        new(tools!, new StreamResolver(StreamResolver.CreateHttpClient()), clock ?? TimeProvider.System, Fast);

    void Dump(RecordingOutcome o)
    {
        output.WriteLine($"stato {o.State}, file {o.Output}, spezzoni {o.Parts.Count}, persi {o.LostMinutes} min");
        if (File.Exists(o.Log)) output.WriteLine(File.ReadAllText(o.Log));
        string logs = Path.Combine(work, "logs");
        if (Directory.Exists(logs))
            foreach (string f in Directory.GetFiles(logs, "*_ffmpeg_*.log").Concat(Directory.GetFiles(logs, "*_unione.log")))
                output.WriteLine($"--- {Path.GetFileName(f)}\n{File.ReadAllText(f)}");
    }

    [Fact]
    public async Task RelinkerInMp4ConNfo()
    {
        if (!Ready()) return;
        var info = new MediaInfo("Prova", 2026, "Trama", ["Documentario"], ImdbId: "tt0000001");
        var o = await NewRecorder().RunAsync(Job(15, info: info), [Cand(7, "/relinker")]);
        Dump(o);
        Assert.Equal("completed", o.State);
        Assert.False(o.MergeFailed);
        Assert.EndsWith(".mp4", o.Output);
        var (d, codecs) = await HlsSimulator.ProbeFileAsync(tools!, o.Output);
        Assert.InRange(d, 10, 30);
        Assert.Contains("h264", codecs);
        Assert.Contains("aac", codecs);
        Assert.Single(o.Parts);
        Assert.Equal(7, o.Parts[0].StreamId);
        Assert.Contains("/hls/live.m3u8", o.Parts[0].Url);            // reindirizzamento seguito
        Assert.False(File.Exists(o.Parts[0].File));                     // .ts tolto dopo l'unione
        Assert.True(File.Exists(Path.ChangeExtension(o.Output, ".nfo")));
    }

    [Fact]
    public async Task UrlSenzaEstensioneConFormatoForzato()
    {
        if (!Ready()) return;
        var o = await NewRecorder().RunAsync(Job(12), [Cand(1, "/noext")]);
        Dump(o);
        Assert.Equal("completed", o.State);
        Assert.Contains("Formato forzato: hls", File.ReadAllText(o.Log));
        Assert.InRange((await HlsSimulator.ProbeFileAsync(tools!, o.Output)).Duration, 7, 30);
    }

    [Fact]
    public async Task ReteCheCadeERipresa()
    {
        if (!Ready()) return;
        var job = Job(40);
        var task = NewRecorder().RunAsync(job, [Cand(1, "/relinker")]);
        await Task.Delay(TimeSpan.FromSeconds(10));
        sim!.Down = true;
        await Task.Delay(TimeSpan.FromSeconds(12));
        sim.Down = false;
        var o = await task;
        Dump(o);
        Assert.Equal("completed", o.State);
        Assert.True(o.Parts.Count >= 2, "dopo l'interruzione serve un nuovo spezzone");
        Assert.InRange((await HlsSimulator.ProbeFileAsync(tools!, o.Output)).Duration, 15, 60);
    }

    [Fact]
    public async Task FlussoDiRiservaDopoDueTentativiSenzaDati()
    {
        if (!Ready()) return;
        var o = await NewRecorder().RunAsync(Job(20), [Cand(1, "/missing"), Cand(2, "/relinker")]);
        Dump(o);
        Assert.Equal("completed", o.State);
        Assert.NotEmpty(o.Parts);
        Assert.All(o.Parts, p => Assert.Equal(2, p.StreamId));
        Assert.Contains("Passo al flusso 2", File.ReadAllText(o.Log));
    }

    [Fact]
    public async Task SoloAudioInMp3()
    {
        if (!Ready()) return;
        var o = await NewRecorder().RunAsync(Job(12, "mp3"), [Cand(1, "/relinker")]);
        Dump(o);
        Assert.Equal("completed", o.State);
        Assert.EndsWith(".mp3", o.Output);
        var (d, codecs) = await HlsSimulator.ProbeFileAsync(tools!, o.Output);
        Assert.Equal(["mp3"], codecs);
        Assert.InRange(d, 7, 30);
    }

    [Fact]
    public async Task ArrestoDellUtente()
    {
        if (!Ready()) return;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var o = await NewRecorder().RunAsync(Job(120), [Cand(1, "/relinker")], stop: stop.Token);
        Dump(o);
        Assert.Equal("stopped", o.State);
        Assert.True(File.Exists(o.Output));
        Assert.InRange((await HlsSimulator.ProbeFileAsync(tools!, o.Output)).Duration, 4, 30);
    }

    // 19/11/2026: «Le relazioni pericolose» interrotta da un riavvio del server ripartiva da _part01.ts (sovrascritto) e
    // lasciava fuori dall'unione lo spezzone interrotto
    [Fact]
    public async Task RipresaDopoRiavvioDelServer_ContinuaGliSpezzoni()
    {
        if (!Ready()) return;
        var job = Job(45);
        using (var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
        {
            try { await NewRecorder().RunAsync(job, [Cand(1, "/relinker")], ct: shutdown.Token); }
            catch (OperationCanceledException) { }      // il server si chiude: la registrazione è abbandonata, senza unire
        }
        string first = Path.Combine(job.OutDir, job.BaseName + "_part01.ts");
        Assert.True(File.Exists(first));
        long firstSize = new FileInfo(first).Length;
        await Task.Delay(TimeSpan.FromSeconds(3));      // il riavvio

        // spezzone interrotto: senza ora di fine, vale l'ultima modifica del file (come fa il programmatore)
        var resume = new List<RecordedPart> { new(1, 1, Cand(1, "/relinker").Url, first, job.Start, File.GetLastWriteTimeUtc(first), "restart") };
        var o = await NewRecorder().RunAsync(job, [Cand(1, "/relinker")], resume: resume);
        Dump(o);
        Assert.Equal("completed", o.State);                                   // pausa sotto il minuto: niente minuti persi
        Assert.True(o.Parts.Count >= 2);
        Assert.Equal(1, o.Parts[0].No);
        Assert.True(o.Parts.Skip(1).All(p => p.No >= 2), "la numerazione continua dopo lo spezzone 1");
        Assert.DoesNotContain(o.Parts.Skip(1), p => p.File == first);       // _part01.ts non viene riscritto
        string log = File.ReadAllText(o.Log);
        Assert.Contains("Registrazione ripresa: 1 spezzone già registrato", log);
        Assert.DoesNotContain("partita in ritardo", log);
        Assert.InRange((await HlsSimulator.ProbeFileAsync(tools!, o.Output)).Duration, 25, 80);   // nel file finale c'è anche lo spezzone 1
        Assert.True(firstSize > 0);
    }

    sealed class JumpClock : TimeProvider
    {
        public TimeSpan Offset;
        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + Offset;
    }

    [Fact]
    public async Task SospensioneDelPcRegistrazioneIncompleta()
    {
        if (!Ready()) return;
        var clock = new JumpClock();
        var job = Job(180, clock: clock);
        var task = NewRecorder(clock).RunAsync(job, [Cand(1, "/relinker")]);
        await Task.Delay(TimeSpan.FromSeconds(8));
        clock.Offset = TimeSpan.FromSeconds(150);       // il PC "dorme" due minuti e mezzo
        var o = await task;
        Dump(o);
        Assert.Equal("incomplete", o.State);
        Assert.InRange(o.LostMinutes, 2, 3);
        Assert.Single(o.LostRanges);
        Assert.Contains("sospensione", File.ReadAllText(o.Log));
    }

    [Fact]
    public async Task NessunFlussoFunzionante()
    {
        if (!Ready()) return;
        var o = await NewRecorder().RunAsync(Job(8), [Cand(1, "/missing")]);
        Dump(o);
        Assert.Equal("failed", o.State);
        Assert.Empty(o.Parts);
    }
}
