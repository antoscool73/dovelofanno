namespace DoveLoFanno.Media;

/// <summary>Esito di una conversione: file scritto, come (copy, audio, video, mp3) o l'errore.</summary>
public sealed record ConversionOutcome(bool Ok, string Output, string Method, string Error);

/// <summary>
/// «Converti...» (20/11/2026, come in RegistraTV): un file video o audio in MP4 o MP3 con ffmpeg. MP4 in tre tentativi,
/// dal più leggero: 1) senza ricodifica; 2) video copiato e audio in AAC; 3) H.264 (veryfast, CRF 21) + AAC. MP3: la
/// prima traccia audio con libmp3lame -q:a 2 (VBR ~190 kbit/s). Il file nuovo va accanto all'originale (che resta), con
/// _2, _3... se il nome c'è già; se quella cartella non si può scrivere (il NAS in sola lettura) va in
/// <c>fallbackDir</c>.
/// </summary>
public static class Converter
{
    public static async Task<ConversionOutcome> ConvertAsync(IMediaTools tools, string source, string format, string fallbackDir, string logBase,
        CancellationToken ct)
    {
        if (!File.Exists(source)) return new(false, "", "", "il file non c'è più: " + source);
        if (format is not ("mp4" or "mp3")) return new(false, "", "", "formato non valido: " + format);
        string output = OutputPath(source, format, fallbackDir);
        Directory.CreateDirectory(Path.GetDirectoryName(logBase)!);

        var attempts = format == "mp3"
            ? new List<(string Method, string[] Args)> { ("mp3", ["-map", "0:a:0", "-vn", "-c:a", "libmp3lame", "-q:a", "2"]) }
            :
            [
                ("copy", ["-map", "0:v:0?", "-map", "0:a?", "-c", "copy", "-movflags", "+faststart"]),
                ("audio", ["-map", "0:v:0?", "-map", "0:a?", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"]),
                ("video", ["-map", "0:v:0?", "-map", "0:a?", "-c:v", "libx264", "-preset", "veryfast", "-crf", "21", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"]),
            ];
        string last = "";
        for (int i = 0; i < attempts.Count; i++)
        {
            var (method, extra) = attempts[i];
            string log = $"{logBase}_{i + 1}.log";
            var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-loglevel", "warning", "-i", source };
            args.AddRange(extra);
            args.Add(output);
            int code;
            try { code = await tools.RunFfmpegAsync(args, log, ct); }
            catch (OperationCanceledException) { TryDelete(output); throw; }
            if (code == 0 && File.Exists(output) && new FileInfo(output).Length > 0) return new(true, output, method, "");
            TryDelete(output);
            last = $"ffmpeg ha risposto {code} ({method})";
        }
        return new(false, "", "", last + ": dettagli nel registro");
    }

    /// <summary>Nome del file nuovo: accanto all'originale se la cartella si scrive, altrimenti in fallbackDir; _2, _3... se c'è già.</summary>
    public static string OutputPath(string source, string format, string fallbackDir)
    {
        string dir = Path.GetDirectoryName(source) ?? fallbackDir;
        if (!Writable(dir)) dir = fallbackDir;
        Directory.CreateDirectory(dir);
        string name = Path.GetFileNameWithoutExtension(source);
        string candidate = Path.Combine(dir, $"{name}.{format}");
        for (int n = 2; File.Exists(candidate) || string.Equals(candidate, source, StringComparison.Ordinal); n++)
            candidate = Path.Combine(dir, $"{name}_{n}.{format}");
        return candidate;
    }

    static bool Writable(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return false;
            string probe = Path.Combine(dir, $".dlf-write-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
