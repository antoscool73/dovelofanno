using System.Text.Json;
using DoveLoFanno.Media;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DoveLoFanno.Tests;

/// <summary>
/// Canale HLS live simulato per i collaudi del registratore (come quelli fatti su Linux per RegistraTV):
/// ffmpeg genera immagine di prova e tono a 25 fps in segmenti da 1 s, un piccolo server locale li serve.
/// Indirizzi: /hls/live.m3u8 (diretto), /relinker (302 come il relinker Rai), /noext (senza estensione, tipo
/// MIME HLS: serve -f hls), /missing (404). <see cref="Down"/> simula la rete che cade.
/// </summary>
public sealed class HlsSimulator : IAsyncDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-hls-" + Guid.NewGuid().ToString("N"));
    IMediaProcess? ffmpeg;
    WebApplication? web;

    public string BaseUrl { get; private set; } = "";
    public volatile bool Down;
    public MediaTools Tools { get; }

    HlsSimulator(MediaTools tools) => Tools = tools;

    /// <summary>ffmpeg e ffprobe, o null se mancano (in CI mancano solo se l'installazione è fallita).</summary>
    public static MediaTools? FindTools()
    {
        try { return MediaTools.Find(); }
        catch (FileNotFoundException)
        {
            if (Environment.GetEnvironmentVariable("DLF_REQUIRE_FFMPEG") == "1") throw;
            return null;
        }
    }

    public static async Task<HlsSimulator> StartAsync(MediaTools tools)
    {
        var s = new HlsSimulator(tools);
        await s.StartInternalAsync();
        return s;
    }

    async Task StartInternalAsync()
    {
        Directory.CreateDirectory(dir);
        ffmpeg = Tools.StartFfmpeg(
        [
            "-hide_banner", "-nostdin", "-loglevel", "warning", "-re",
            "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
            "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-g", "25", "-b:v", "600k",
            "-c:a", "aac", "-b:a", "96k",
            "-f", "hls", "-hls_time", "1", "-hls_list_size", "6", "-hls_flags", "delete_segments+omit_endlist+independent_segments",
            Path.Combine(dir, "live.m3u8"),
        ], Path.Combine(dir, "generator.log"));

        var b = WebApplication.CreateSlimBuilder();
        b.Logging.ClearProviders();
        b.WebHost.UseUrls("http://127.0.0.1:0");
        web = b.Build();
        web.MapGet("/hls/{file}", (string file) => Down ? Results.StatusCode(503) : Serve(file));
        web.MapGet("/relinker", (HttpContext h) => Down ? Results.StatusCode(503) : Results.Redirect("/hls/live.m3u8"));
        web.MapGet("/noext", () =>
        {
            if (Down) return Results.StatusCode(503);
            string text = ReadShared(Path.Combine(dir, "live.m3u8"));
            var lines = text.Split('\n').Select(l => l.Length > 0 && !l.StartsWith('#') ? "/hls/" + l.Trim() : l);
            return Results.Text(string.Join('\n', lines), "application/vnd.apple.mpegurl");
        });
        web.MapGet("/missing", () => Results.NotFound());
        await web.StartAsync();
        BaseUrl = web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        // pronto quando la playlist ha almeno 3 segmenti
        var until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until)
        {
            string p = Path.Combine(dir, "live.m3u8");
            if (File.Exists(p) && ReadShared(p).Split('\n').Count(l => l.EndsWith(".ts", StringComparison.Ordinal)) >= 3) return;
            await Task.Delay(250);
        }
        throw new TimeoutException("il generatore HLS non è partito: " + ReadShared(Path.Combine(dir, "generator.log")));
    }

    IResult Serve(string file)
    {
        string path = Path.Combine(dir, Path.GetFileName(file));
        if (!File.Exists(path)) return Results.NotFound();
        byte[] data;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            data = ms.ToArray();
        }
        catch (IOException) { return Results.NotFound(); }
        return Results.Bytes(data, file.EndsWith(".m3u8", StringComparison.Ordinal) ? "application/vnd.apple.mpegurl" : "video/mp2t");
    }

    static string ReadShared(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }
        catch (IOException) { return ""; }
    }

    /// <summary>Durata e codec di un file registrato.</summary>
    public static async Task<(double Duration, string[] Codecs)> ProbeFileAsync(MediaTools tools, string file)
    {
        var (code, stdout, stderr) = await tools.RunFfprobeAsync(
            ["-v", "error", "-show_entries", "format=duration:stream=codec_name", "-of", "json", file], TimeSpan.FromSeconds(30), default);
        if (code != 0) throw new InvalidOperationException("ffprobe: " + stderr);
        using var doc = JsonDocument.Parse(stdout);
        double d = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var codecs = doc.RootElement.GetProperty("streams").EnumerateArray().Select(s => s.GetProperty("codec_name").GetString()!).ToArray();
        return (d, codecs);
    }

    public async ValueTask DisposeAsync()
    {
        if (web != null) await web.DisposeAsync();
        if (ffmpeg != null)
        {
            ffmpeg.Kill();
            await ffmpeg.WaitForExitAsync(TimeSpan.FromSeconds(10));
            ffmpeg.Dispose();
        }
        try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
