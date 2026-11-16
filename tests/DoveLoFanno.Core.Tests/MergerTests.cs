using DoveLoFanno.Media;
using DoveLoFanno.Recording;
using Xunit.Abstractions;

namespace DoveLoFanno.Tests;

/// <summary>
/// Unione degli spezzoni quando uno finisce con un pacchetto audio tronco, come dopo una caduta della rete:
/// con ffmpeg 9.0.2 l'unione senza +discardcorrupt falliva ("Input packet too small"), trovato nel collaudo del 16/11.
/// </summary>
[Collection("ffmpeg")]
public sealed class MergerTests(ITestOutputHelper output) : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-merge-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Lascia nell'ultimo pacchetto audio (PES con stream id 0xC0) solo l'intestazione e 2 byte, come uno spezzone interrotto.</summary>
    static void TruncateLastAudioPacket(string ts)
    {
        byte[] data = File.ReadAllBytes(ts);
        int n = data.Length / 188;
        for (int idx = n - 1; idx > 0; idx--)
        {
            int o = idx * 188;
            if ((data[o + 1] & 0x40) == 0) continue;
            int off = 4;
            if ((data[o + 3] & 0x20) != 0) off += 1 + data[o + 4];
            if (off + 9 > 188 || data[o + off] != 0 || data[o + off + 1] != 0 || data[o + off + 2] != 1 || data[o + off + 3] != 0xC0) continue;
            int pesh = 9 + data[o + off + 8];
            byte[] keep = data.AsSpan(o + off, pesh + 2).ToArray();
            int l = 188 - 5 - keep.Length;
            var p = new byte[188];
            Array.Copy(data, o, p, 0, 4);
            p[3] = (byte)((p[3] & 0xCF) | 0x30);   // campo di adattamento + dati
            p[4] = (byte)l;
            p[5] = 0;
            for (int i = 6; i < 5 + l; i++) p[i] = 0xFF;
            Array.Copy(keep, 0, p, 5 + l, keep.Length);
            using var f = File.Create(ts);
            f.Write(data, 0, o);
            f.Write(p);
            return;
        }
        throw new InvalidOperationException("nessun pacchetto audio trovato");
    }

    [Fact]
    public async Task SpezzoneConPacchettoAudioTroncoSiUnisceLostesso()
    {
        var tools = HlsSimulator.FindTools();
        if (tools == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }
        Directory.CreateDirectory(dir);
        string a = Path.Combine(dir, "Prova_part01.ts"), b = Path.Combine(dir, "Prova_part02.ts");
        int code = await tools.RunFfmpegAsync(
        [
            "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
            "-t", "6", "-c:v", "libx264", "-preset", "ultrafast", "-g", "25", "-c:a", "aac", "-b:a", "96k", "-f", "mpegts", a,
        ], Path.Combine(dir, "gen.log"), default);
        Assert.Equal(0, code);
        File.Copy(a, b);
        TruncateLastAudioPacket(a);

        var job = new RecordingJob(1, "Prova", "Canale", DateTimeOffset.Now, DateTimeOffset.Now.AddMinutes(1), "mp4", dir, "Prova", dir);
        var log = new List<string>();
        string? mp4 = await Merger.MergeAsync(tools, [a, b], Path.Combine(dir, "Prova"), job, log.Add, default);
        foreach (var l in log) output.WriteLine(l);
        Assert.NotNull(mp4);
        var (d, codecs) = await HlsSimulator.ProbeFileAsync(tools, mp4!);
        Assert.InRange(d, 11, 13);
        Assert.Contains("aac", codecs);
        Assert.DoesNotContain(log, l => l.Contains("ricodificando"));   // basta lo scarto del pacchetto corrotto
    }
}
