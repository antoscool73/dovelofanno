using System.Diagnostics;

namespace DoveLoFanno.Media;

/// <summary>Un processo ffmpeg in corso.</summary>
public interface IMediaProcess : IDisposable
{
    bool HasExited { get; }
    int ExitCode { get; }
    void Kill();
    Task WaitForExitAsync(TimeSpan timeout);
}

/// <summary>Avvio di ffmpeg e ffprobe; sostituibile nei test.</summary>
public interface IMediaTools
{
    /// <summary>Avvia ffmpeg con stderr nel file di log (stdout scartato).</summary>
    IMediaProcess StartFfmpeg(IReadOnlyList<string> args, string logPath);

    /// <summary>Esegue ffprobe e restituisce codice di uscita, stdout e stderr.</summary>
    Task<(int ExitCode, string Stdout, string Stderr)> RunFfprobeAsync(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct);

    /// <summary>Esegue ffmpeg fino alla fine (unioni, conversioni), stderr nel log.</summary>
    Task<int> RunFfmpegAsync(IReadOnlyList<string> args, string logPath, CancellationToken ct);
}

/// <summary>ffmpeg e ffprobe veri: dal percorso indicato, dal PATH o (Windows) dalla cartella di winget.</summary>
public sealed class MediaTools(string ffmpegPath, string ffprobePath) : IMediaTools
{
    public string FfmpegPath { get; } = ffmpegPath;
    public string FfprobePath { get; } = ffprobePath;

    public static MediaTools Find(string? ffmpegHint = null)
    {
        string ffmpeg = Locate("ffmpeg", ffmpegHint) ?? throw new FileNotFoundException("ffmpeg non trovato");
        string probeHint = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        string ffprobe = Locate("ffprobe", probeHint) ?? throw new FileNotFoundException("ffprobe non trovato");
        return new MediaTools(ffmpeg, ffprobe);
    }

    static string? Locate(string name, string? hint)
    {
        if (!string.IsNullOrEmpty(hint) && File.Exists(hint)) return hint;
        string exe = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            string p = Path.Combine(dir.Trim('"'), exe);
            if (File.Exists(p)) return p;
        }
        if (OperatingSystem.IsWindows())
        {
            string winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", exe);
            if (File.Exists(winget)) return winget;
        }
        return null;
    }

    public IMediaProcess StartFfmpeg(IReadOnlyList<string> args, string logPath)
    {
        var psi = Info(FfmpegPath, args);
        var p = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg non avviato");
        return new Running(p, logPath);
    }

    public async Task<int> RunFfmpegAsync(IReadOnlyList<string> args, string logPath, CancellationToken ct)
    {
        using var p = new Running(Process.Start(Info(FfmpegPath, args)) ?? throw new InvalidOperationException("ffmpeg non avviato"), logPath);
        try { await p.Process.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { p.Kill(); throw; }
        await p.Drained;
        return p.Process.ExitCode;
    }

    public async Task<(int, string, string)> RunFfprobeAsync(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = Info(FfprobePath, args);
        psi.RedirectStandardOutput = true;
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ffprobe non avviato");
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch (InvalidOperationException) { }
            if (ct.IsCancellationRequested) throw;
            return (-1, "", "timeout di ffprobe");
        }
        return (p.ExitCode, await stdout, await stderr);
    }

    static ProcessStartInfo Info(string exe, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    sealed class Running : IMediaProcess
    {
        public Process Process { get; }
        public Task Drained { get; }

        public Running(Process p, string logPath)
        {
            Process = p;
            p.StandardInput.Close();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
            Drained = Task.Run(async () =>
            {
                await using var log = new StreamWriter(logPath, append: true);
                await p.StandardError.BaseStream.CopyToAsync(log.BaseStream);
            });
        }

        public bool HasExited => Process.HasExited;
        public int ExitCode => Process.ExitCode;

        public void Kill()
        {
            try { if (!Process.HasExited) Process.Kill(true); } catch (InvalidOperationException) { }
        }

        public async Task WaitForExitAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try { await Process.WaitForExitAsync(cts.Token); await Drained.WaitAsync(cts.Token); }
            catch (OperationCanceledException) { }
        }

        public void Dispose() => Process.Dispose();
    }
}
