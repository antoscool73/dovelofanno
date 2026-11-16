using System.Text.Json;
using DoveLoFanno.Media;

namespace DoveLoFanno.Sources;

/// <summary>Stato di un flusso, come nella colonna streams.state.</summary>
public static class StreamState
{
    public const string Unchecked = "unchecked";
    public const string Ok = "ok";
    public const string Drm = "drm";
    public const string Unreachable = "unreachable";
    public const string Geo = "geo";
    public const string Limit = "limit";
}

/// <summary>Esito del controllo di un flusso con ffprobe.</summary>
public sealed record ProbeResult(string State, string Kind, int? Width, int? Height, string VideoCodec, string AudioCodec, string Detail);

/// <summary>
/// Controllo di un flusso con ffprobe (pochi secondi): funziona (con tipo, risoluzione e codec), DRM,
/// non raggiungibile, bloccato per zona (403 o 451), troppe connessioni (429 o 509).
/// </summary>
public sealed class StreamProber(IMediaTools tools, StreamResolver resolver)
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

    public async Task<ProbeResult> ProbeAsync(string url, string userAgent, string referrer, CancellationToken ct = default)
    {
        var src = await resolver.ResolveAsync(url, userAgent, referrer, ct);
        var args = new List<string> { "-hide_banner", "-v", "error", "-rw_timeout", "15000000" };
        if (userAgent.Length > 0) args.AddRange(["-user_agent", userAgent]);
        if (referrer.Length > 0) args.AddRange(["-referer", referrer]);
        if (src.Format.Length > 0) args.AddRange(["-f", src.Format]);
        args.AddRange(["-analyzeduration", "5000000", "-probesize", "5000000", "-show_entries",
            "stream=codec_type,codec_name,width,height:format=format_name", "-of", "json", src.Url]);
        var (code, stdout, stderr) = await tools.RunFfprobeAsync(args, Timeout, ct);
        return Classify(code, stdout, stderr);
    }

    public static ProbeResult Classify(int exitCode, string stdout, string stderr)
    {
        string err = stderr.Trim();
        string detail = err.Length > 300 ? err[..300] : err;
        if (exitCode != 0 || stdout.Trim().Length == 0)
        {
            string state = err.Contains("403") || err.Contains("451") ? StreamState.Geo
                : err.Contains("429") || err.Contains("509") || err.Contains("Too Many", StringComparison.OrdinalIgnoreCase) ? StreamState.Limit
                : err.Contains("DRM", StringComparison.OrdinalIgnoreCase) || err.Contains("encrypt", StringComparison.OrdinalIgnoreCase) ? StreamState.Drm
                : StreamState.Unreachable;
            return new ProbeResult(state, "unknown", null, null, "", "", detail);
        }
        string kind = "unknown", vcodec = "", acodec = "";
        int? w = null, h = null;
        using (var doc = JsonDocument.Parse(stdout))
        {
            var root = doc.RootElement;
            if (root.TryGetProperty("format", out var f) && f.TryGetProperty("format_name", out var fn))
            {
                string n = fn.GetString() ?? "";
                kind = n.Contains("hls") ? "hls" : n.Contains("dash") ? "dash" : n.Contains("mpegts") ? "ts" : "unknown";
            }
            if (root.TryGetProperty("streams", out var ss))
                foreach (var s in ss.EnumerateArray())
                {
                    string type = s.TryGetProperty("codec_type", out var t) ? t.GetString() ?? "" : "";
                    string codec = s.TryGetProperty("codec_name", out var c) ? c.GetString() ?? "" : "";
                    if (type == "video" && vcodec.Length == 0)
                    {
                        vcodec = codec;
                        if (s.TryGetProperty("width", out var ww) && ww.TryGetInt32(out int wi)) w = wi;
                        if (s.TryGetProperty("height", out var hh) && hh.TryGetInt32(out int hi)) h = hi;
                    }
                    else if (type == "audio" && acodec.Length == 0) acodec = codec;
                }
        }
        if (vcodec.Length == 0 && acodec.Length == 0) return new ProbeResult(StreamState.Unreachable, kind, null, null, "", "", "nessuna traccia audio o video");
        return new ProbeResult(StreamState.Ok, kind, w, h, vcodec, acodec, detail);
    }
}
