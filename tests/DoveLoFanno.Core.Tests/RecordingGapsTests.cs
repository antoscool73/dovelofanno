using DoveLoFanno.Media;
using DoveLoFanno.Recording;
using Xunit.Abstractions;

namespace DoveLoFanno.Tests;

/// <summary>
/// Minuti persi per i buchi del flusso e per i salti dentro uno spezzone (26/11/2026): prima contavano solo sospensione
/// e partenza in ritardo, e «X Factor Live 5» (UN62) senza ~66 minuti risultava completata con 0 minuti persi.
/// </summary>
public sealed class GapScannerTests(ITestOutputHelper output) : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-gap-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static async Task Make(MediaTools tools, string file, double offset)
    {
        string[] args =
        [
            "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=25", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
            "-t", "3", "-c:v", "libx264", "-preset", "ultrafast", "-g", "25", "-c:a", "aac",
            "-output_ts_offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture), "-f", "mpegts", file,
        ];
        Assert.Equal(0, await tools.RunFfmpegAsync(args, file + ".log", default));
    }

    [Fact]
    public async Task SaltoDiCinqueSecondiNelVideoENellAudio()
    {
        var tools = HlsSimulator.FindTools();
        if (tools == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }
        Directory.CreateDirectory(dir);
        string a = Path.Combine(dir, "a.ts"), b = Path.Combine(dir, "b.ts"), joined = Path.Combine(dir, "salto.ts");
        await Make(tools, a, 0);
        await Make(tools, b, 8);            // finisce a ~3 s, riparte a ~8 s: mancano ~5 s
        await using (var o = File.Create(joined))
        {
            foreach (var f in new[] { a, b }) { await using var i = File.OpenRead(f); await i.CopyToAsync(o); }
        }

        Assert.Empty(await GapScanner.ScanAsync(tools, a, default));
        var gaps = await GapScanner.ScanAsync(tools, joined, default);
        foreach (var g in gaps) output.WriteLine($"{g.Stream} a {g.At} s: {g.Seconds} s");
        Assert.Contains(gaps, g => g.Stream == "video" && g.Seconds is > 4 and < 6.5 && g.At is > 2 and < 4);
        Assert.Contains(gaps, g => g.Stream == "audio" && g.Seconds is > 4 and < 6.5);
    }
}

public sealed class RecorderGapTests(ITestOutputHelper output) : RecorderTestBase(output)
{
    // la rete cade per 40 s: il nuovo spezzone riparte, ma i ~40 s senza dati sono persi
    [Fact]
    public async Task BuchiDelFlussoContatiTraIMinutiPersi()
    {
        if (!Ready()) return;
        var job = Job(75);
        var task = NewRecorder().RunAsync(job, [Cand(1, "/relinker")]);
        await Task.Delay(TimeSpan.FromSeconds(12));
        sim!.Down = true;
        await Task.Delay(TimeSpan.FromSeconds(40));
        sim.Down = false;
        var o = await task;
        Dump(o);
        Assert.True(o.Parts.Count >= 2, "dopo l'interruzione serve un nuovo spezzone");
        var r = Assert.Single(o.LostRanges);
        Assert.InRange((r.To - r.From).TotalSeconds, 30, 70);
        Assert.Contains("Nessun dato registrato", File.ReadAllText(o.Log));
    }
}
