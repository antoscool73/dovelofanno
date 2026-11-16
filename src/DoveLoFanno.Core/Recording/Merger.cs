using System.Text;
using DoveLoFanno.Media;

namespace DoveLoFanno.Recording;

/// <summary>
/// Unione degli spezzoni .ts, portata da Merge-Parts: in MP4 senza ricodifica (concat, -c copy, +faststart),
/// oppure solo audio in MP3 (libmp3lame -q:a 2). L'esito vale solo se il file finale non è troppo più piccolo
/// degli spezzoni (MP4 almeno metà, MP3 almeno un quinto). Con la chiusura forzata di ffmpeg l'ultimo pacchetto
/// è tronco: gli avvisi "Packet corrupt" nel log di unione sono innocui.
/// </summary>
public static class Merger
{
    public static async Task<string?> MergeAsync(IMediaTools tools, IReadOnlyList<string> parts, string basePath, RecordingJob job, Action<string> log, CancellationToken ct)
    {
        bool mp3 = job.AudioOnly;
        string ext = mp3 ? ".mp3" : ".mp4";
        double minRatio = mp3 ? 0.2 : 0.5;
        string output = FreeOutputPath(basePath, ext);
        string list = Path.Combine(job.LogDir, $"{job.Id}_concat.txt");
        await File.WriteAllLinesAsync(list, parts.Select(p => "file '" + Path.GetFullPath(p).Replace("'", "'\\''") + "'"), new UTF8Encoding(false), ct);
        string mergeLog = Path.Combine(job.LogDir, $"{job.Id}_unione.log");
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "warning", "-y", "-f", "concat", "-safe", "0", "-i", list };
        args.AddRange(mp3
            ? ["-map", "0:a:0", "-vn", "-c:a", "libmp3lame", "-q:a", "2"]
            : ["-map", "0", "-c", "copy", "-avoid_negative_ts", "make_zero", "-movflags", "+faststart"]);
        args.Add(output);
        log($"Unione di {parts.Count} spezzone/i in {output}");
        int code = await tools.RunFfmpegAsync(args, mergeLog, ct);
        long total = parts.Sum(p => File.Exists(p) ? new FileInfo(p).Length : 0);
        if (code == 0 && File.Exists(output) && new FileInfo(output).Length > total * minRatio)
        {
            Recorder.TryDelete(list);
            return output;
        }
        log($"Unione non riuscita (codice {code}), vedi {mergeLog}");
        Recorder.TryDelete(output);
        return null;
    }

    /// <summary>base.ext, oppure base_2.ext, base_3.ext... se esiste già.</summary>
    public static string FreeOutputPath(string basePath, string ext)
    {
        string p = basePath + ext;
        for (int n = 2; File.Exists(p); n++) p = $"{basePath}_{n}{ext}";
        return p;
    }
}
