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
        long total = parts.Sum(p => File.Exists(p) ? new FileInfo(p).Length : 0);
        log($"Unione di {parts.Count} spezzone/i in {output}");

        // Uno spezzone chiuso di colpo (rete caduta, ffmpeg fermato all'orario di fine) può finire con un pacchetto audio
        // tronco: senza +discardcorrupt il filtro aac_adtstoasc lo rifiuta ("Input packet too small") e l'unione in MP4
        // fallisce del tutto (trovato nel collaudo del 16/11 con ffmpeg 9.0.2). Se fallisse comunque, si riprova
        // ricodificando solo l'audio.
        string[] mp3Args = ["-map", "0:a:0", "-vn", "-c:a", "libmp3lame", "-q:a", "2"];
        string[] copyArgs = ["-map", "0", "-c", "copy", "-avoid_negative_ts", "make_zero", "-movflags", "+faststart"];
        string[] audioArgs = ["-map", "0:v:0?", "-map", "0:a:0?", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-avoid_negative_ts", "make_zero", "-movflags", "+faststart"];
        List<string[]> attempts = mp3 ? [mp3Args] : [copyArgs, audioArgs];
        int code = 0;
        for (int i = 0; i < attempts.Count; i++)
        {
            var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "warning", "-y", "-fflags", "+discardcorrupt", "-f", "concat", "-safe", "0", "-i", list };
            args.AddRange(attempts[i]);
            args.Add(output);
            if (i > 0) log("Unione senza ricodifica non riuscita: riprovo ricodificando solo l'audio");
            code = await tools.RunFfmpegAsync(args, mergeLog, ct);
            if (code == 0 && File.Exists(output) && new FileInfo(output).Length > total * minRatio)
            {
                Recorder.TryDelete(list);
                return output;
            }
            Recorder.TryDelete(output);
        }
        log($"Unione non riuscita (codice {code}), vedi {mergeLog}");
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
