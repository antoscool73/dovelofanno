using System.Globalization;
using System.Text.Json;
using DoveLoFanno.Media;

namespace DoveLoFanno.Library;

/// <summary>Una traccia audio o di sottotitoli: Index è il numero tra le tracce dello stesso tipo (0:a:N, 0:s:N).</summary>
public sealed record TrackInfo(int Index, string Codec, string Language, string Title);

/// <summary>Quello che ffprobe dice di un file della libreria.</summary>
public sealed record ProbeInfo(
    double DurationS, string Container, string VideoCodec, string AudioCodec, int? Width, int? Height,
    IReadOnlyList<TrackInfo> Audio, IReadOnlyList<TrackInfo> Subtitles);

/// <summary>Come si guarda un file nel browser.</summary>
public enum Playback
{
    /// <summary>Il browser lo legge così com'è (MP4 H.264 + AAC, il caso delle registrazioni).</summary>
    Direct,
    /// <summary>Video e audio vanno bene, cambia solo il contenitore (MKV, TS, AVI con H.264 e AAC): copia senza ricodifica.</summary>
    Remux,
    /// <summary>Il video va bene, l'audio no (AC3, MP2, DTS): si ricodifica solo l'audio in AAC.</summary>
    Audio,
    /// <summary>Il video non va bene (HEVC, MPEG-2, XviD): ricodifica completa in H.264, la più lenta.</summary>
    Full,
}

public static class MediaProbe
{
    /// <summary>Legge durata, codec e tracce; null se ffprobe non riesce a leggere il file.</summary>
    public static async Task<ProbeInfo?> ProbeAsync(IMediaTools tools, string path, CancellationToken ct)
    {
        var (code, stdout, _) = await tools.RunFfprobeAsync(
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path], TimeSpan.FromSeconds(60), ct);
        return code == 0 ? Parse(stdout) : null;
    }

    public static ProbeInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            double duration = 0;
            string container = "";
            if (root.TryGetProperty("format", out var f))
            {
                duration = Num(f, "duration");
                container = Str(f, "format_name");
            }
            string video = "", audio = "";
            int? w = null, h = null;
            var audios = new List<TrackInfo>();
            var subs = new List<TrackInfo>();
            if (root.TryGetProperty("streams", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var s in arr.EnumerateArray())
                {
                    string type = Str(s, "codec_type"), codec = Str(s, "codec_name");
                    string lang = "", title = "";
                    if (s.TryGetProperty("tags", out var tags))
                    {
                        lang = Str(tags, "language");
                        title = Str(tags, "title");
                    }
                    bool cover = s.TryGetProperty("disposition", out var d) && d.TryGetProperty("attached_pic", out var ap) && ap.ValueKind == JsonValueKind.Number && ap.GetInt32() == 1;
                    switch (type)
                    {
                        case "video" when !cover && video.Length == 0:
                            video = codec;
                            w = (int)Num(s, "width") is > 0 and var ww ? ww : null;
                            h = (int)Num(s, "height") is > 0 and var hh ? hh : null;
                            if (duration <= 0) duration = Num(s, "duration");
                            break;
                        case "audio":
                            if (audio.Length == 0) audio = codec;
                            audios.Add(new TrackInfo(audios.Count, codec, lang, title));
                            break;
                        case "subtitle":
                            subs.Add(new TrackInfo(subs.Count, codec, lang, title));
                            break;
                    }
                }
            if (video.Length == 0 && audio.Length == 0) return null;
            return new ProbeInfo(duration, container, video, audio, w, h, audios, subs);
        }
        catch (JsonException) { return null; }
    }

    static readonly HashSet<string> BrowserAudio = new(StringComparer.OrdinalIgnoreCase) { "aac", "mp3", "opus", "vorbis", "flac" };
    static readonly HashSet<string> TextSubtitles = new(StringComparer.OrdinalIgnoreCase) { "subrip", "srt", "ass", "ssa", "mov_text", "webvtt", "text" };

    /// <summary>Sottotitoli che si possono trasformare in WebVTT (non quelli a immagini dei DVD e Blu-ray).</summary>
    public static bool IsTextSubtitle(string codec) => TextSubtitles.Contains(codec);

    /// <summary>Decide se il browser legge il file così com'è o se serve una copia, e quale.</summary>
    public static Playback Decide(string path, ProbeInfo p)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        bool mp4 = ext is ".mp4" or ".m4v" or ".mov" && p.Container.Contains("mp4", StringComparison.OrdinalIgnoreCase);
        bool webm = ext == ".webm";
        bool audioOnly = p.VideoCodec.Length == 0;
        if (audioOnly) return ext is ".mp3" or ".m4a" or ".aac" or ".ogg" or ".opus" || (mp4 && BrowserAudio.Contains(p.AudioCodec)) ? Playback.Direct : Playback.Audio;

        bool videoOk = p.VideoCodec.Equals("h264", StringComparison.OrdinalIgnoreCase)
            || (webm && p.VideoCodec is "vp8" or "vp9" or "av1");
        // con più tracce audio la prima deve essere quella italiana, altrimenti si fa la copia che la mette davanti
        bool audioOk = p.AudioCodec.Length == 0 || (BrowserAudio.Contains(p.AudioCodec) && p.Audio.All(a => BrowserAudio.Contains(a.Codec)));
        bool italianFirst = ItalianIndex(p) is null or 0;
        if (!videoOk) return Playback.Full;
        if (!audioOk) return Playback.Audio;
        if ((mp4 || webm) && italianFirst) return Playback.Direct;
        return Playback.Remux;
    }

    /// <summary>Numero della traccia audio italiana, se il file ne ha più di una e una è italiana.</summary>
    public static int? ItalianIndex(ProbeInfo p) =>
        p.Audio.Count > 1 && p.Audio.FirstOrDefault(a => a.Language is "ita" or "it" or "italian") is { } it ? it.Index : null;

    /// <summary>Argomenti di ffmpeg per la copia per il browser (MP4 con +faststart, italiano come prima traccia audio).</summary>
    public static List<string> CopyArgs(string input, string output, ProbeInfo p, Playback how)
    {
        var a = new List<string> { "-hide_banner", "-y", "-i", input, "-map", "0:v:0?" };
        int? ita = ItalianIndex(p);
        if (ita is int i) a.AddRange(["-map", $"0:a:{i.ToString(CultureInfo.InvariantCulture)}"]);
        for (int k = 0; k < p.Audio.Count; k++)
            if (k != ita) a.AddRange(["-map", $"0:a:{k.ToString(CultureInfo.InvariantCulture)}"]);
        if (how == Playback.Full) a.AddRange(["-c:v", "libx264", "-preset", "veryfast", "-crf", "21", "-pix_fmt", "yuv420p"]);
        else a.AddRange(["-c:v", "copy"]);
        if (how is Playback.Audio or Playback.Full) a.AddRange(["-c:a", "aac", "-b:a", "192k"]);
        else a.AddRange(["-c:a", "copy"]);
        a.AddRange(["-sn", "-dn", "-threads", "2", "-movflags", "+faststart", "-f", "mp4", output]);
        return a;
    }

    /// <summary>SubRip in WebVTT, per i sottotitoli .srt accanto al file.</summary>
    public static string SrtToVtt(string srt)
    {
        var sb = new System.Text.StringBuilder("WEBVTT\n\n");
        foreach (string raw in srt.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = raw.TrimStart('﻿');
            if (line.Contains("-->", StringComparison.Ordinal)) line = line.Replace(',', '.');
            sb.Append(line).Append('\n');
        }
        return sb.ToString();
    }

    static string Str(JsonElement m, string k) =>
        m.TryGetProperty(k, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            _ => "",
        } : "";

    static double Num(JsonElement m, string k)
    {
        if (!m.TryGetProperty(k, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
        return 0;
    }
}
