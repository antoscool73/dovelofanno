using DoveLoFanno.Data;
using DoveLoFanno.Media;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace DoveLoFanno.Tests;

/// <summary>«Converti...» (20/11/2026) con ffmpeg vero, come i collaudi di RegistraTV del 12/11.</summary>
public sealed class ConverterTests(ITestOutputHelper output) : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-conv-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    async Task<string> Make(MediaTools tools, string name, params string[] codec)
    {
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, name);
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-loglevel", "error",
            "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25:duration=3", "-f", "lavfi", "-i", "sine=frequency=440:duration=3" };
        args.AddRange(codec);
        args.Add(file);
        Assert.Equal(0, await tools.RunFfmpegAsync(args, Path.Combine(dir, name + ".log"), default));
        return file;
    }

    [Fact]
    public async Task TsInMp4SenzaRicodifica_PoiMp3_NomiNuovi()
    {
        var tools = HlsSimulator.FindTools();
        if (tools == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }
        string ts = await Make(tools, "Prova d'esempio.ts", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-f", "mpegts");
        string fallback = Path.Combine(dir, "Convertiti");

        var mp4 = await Converter.ConvertAsync(tools, ts, "mp4", fallback, Path.Combine(dir, "logs", "c1"), default);
        Assert.True(mp4.Ok, mp4.Error);
        Assert.Equal(("copy", Path.Combine(dir, "Prova d'esempio.mp4")), (mp4.Method, mp4.Output));
        Assert.True(File.Exists(ts));                                                   // l'originale resta

        var again = await Converter.ConvertAsync(tools, ts, "mp4", fallback, Path.Combine(dir, "logs", "c2"), default);
        Assert.Equal(Path.Combine(dir, "Prova d'esempio_2.mp4"), again.Output);        // nome già usato: _2

        var mp3 = await Converter.ConvertAsync(tools, mp4.Output, "mp3", fallback, Path.Combine(dir, "logs", "c3"), default);
        Assert.True(mp3.Ok, mp3.Error);
        Assert.Equal("mp3", mp3.Method);
        var (d, codecs) = await HlsSimulator.ProbeFileAsync(tools, mp3.Output);
        Assert.Equal(["mp3"], codecs);
        Assert.InRange(d, 2, 4);
    }

    [Fact]
    public async Task WebmInMp4_Ricodificato_EFileMancante()
    {
        var tools = HlsSimulator.FindTools();
        if (tools == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }
        string webm = await Make(tools, "clip.webm", "-c:v", "libvpx", "-b:v", "300k", "-c:a", "libvorbis");
        var o = await Converter.ConvertAsync(tools, webm, "mp4", Path.Combine(dir, "Convertiti"), Path.Combine(dir, "logs", "w"), default);
        Assert.True(o.Ok, o.Error);
        Assert.Contains(o.Method, new[] { "audio", "video" });                         // VP8/Vorbis: almeno l'audio va ricodificato
        var (_, codecs) = await HlsSimulator.ProbeFileAsync(tools, o.Output);
        Assert.Contains("aac", codecs);

        var missing = await Converter.ConvertAsync(tools, Path.Combine(dir, "non-c-e.ts"), "mp4", dir, Path.Combine(dir, "logs", "m"), default);
        Assert.False(missing.Ok);
        Assert.Contains("non c'è", missing.Error);
    }

    [Fact]
    public void CodaUnaAllaVolta_AnnullaSoloInCoda_RipresaDopoRiavvio()
    {
        Directory.CreateDirectory(dir);
        var store = new ConversionStore(Path.Combine(dir, "dlf.db"));
        var now = DateTimeOffset.UtcNow;
        long a = store.Add(null, "/x/a.ts", "A", "mp4", now), b = store.Add(null, "/x/b.ts", "B", "mp3", now);
        Assert.Equal(a, store.TakeNext()!.Id);
        Assert.False(store.Cancel(a));                                                  // già partita
        Assert.True(store.Cancel(b));
        Assert.Null(store.TakeNext());
        Assert.Equal(1, store.Requeue());                                               // il server riparte: A torna in coda
        store.Finish(store.TakeNext()!.Id, true, "/x/a.mp4", "copy", "", "/log", now);
        Assert.Equal(["cancelled", "done"], store.List().Select(r => r.State));
    }
}
