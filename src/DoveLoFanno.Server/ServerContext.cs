using DoveLoFanno.Data;
using DoveLoFanno.Media;
using DoveLoFanno.Recording;
using DoveLoFanno.Sources;

namespace DoveLoFanno.Server;

/// <summary>Cartelle e servizi del server, creati una volta all'avvio.</summary>
public sealed class ServerContext : IDisposable
{
    public string DataDir { get; }
    public string DbPath { get; }
    public string RecordingsDir { get; }
    public string LogDir { get; }
    public IMediaTools Tools { get; }
    public HttpClient Http { get; }
    public TimeProvider Clock { get; }
    public SourceStore Sources { get; }
    public RecordingStore Recordings { get; }
    public SettingsStore Settings { get; }
    public StreamResolver Resolver { get; }
    public StreamProber Prober { get; }
    public RecordingScheduler Scheduler { get; }
    public Backup Backups { get; }
    readonly IKeepAwake keepAwake;

    public ServerContext(IConfiguration config, IMediaTools? tools = null, TimeProvider? clock = null, RecorderOptions? recorderOptions = null)
    {
        var section = config.GetSection("DoveLoFanno");
        DataDir = Full(section["DataDir"], DefaultDataDir());
        RecordingsDir = Full(section["RecordingsDir"], Path.Combine(DataDir, "registrazioni"));
        LogDir = Path.Combine(DataDir, "logs");
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(RecordingsDir);
        Directory.CreateDirectory(LogDir);
        DbPath = Path.Combine(DataDir, "dlf.db");
        Database.Open(DbPath).Dispose();     // crea o aggiorna lo schema subito, non alla prima richiesta

        Tools = tools ?? MediaTools.Find(section["Ffmpeg"]);
        Http = StreamResolver.CreateHttpClient();
        Clock = clock ?? TimeProvider.System;
        Sources = new SourceStore(DbPath);
        Recordings = new RecordingStore(DbPath);
        Settings = new SettingsStore(DbPath);
        Resolver = new StreamResolver(Http);
        Prober = new StreamProber(Tools, Resolver);
        keepAwake = OperatingSystem.IsWindows() ? new WindowsKeepAwake() : NoKeepAwake.Instance;
        var recorder = new Recorder(Tools, Resolver, Clock, recorderOptions, keepAwake);
        Scheduler = new RecordingScheduler(Recordings, Sources, recorder, Http, Clock, new SchedulerOptions(RecordingsDir, LogDir));
        Backups = new Backup(DbPath, Path.Combine(DataDir, "backup"));
    }

    static string Full(string? configured, string fallback) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? fallback : Environment.ExpandEnvironmentVariables(configured));

    /// <summary>Windows: %LOCALAPPDATA%\DoveLoFanno (mai la cartella di RegistraTV); container: /data; altrimenti ./data.</summary>
    static string DefaultDataDir()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DoveLoFanno");
        return Directory.Exists("/data") ? "/data" : Path.Combine(AppContext.BaseDirectory, "data");
    }

    public void Dispose()
    {
        (keepAwake as IDisposable)?.Dispose();
        Http.Dispose();
    }
}
