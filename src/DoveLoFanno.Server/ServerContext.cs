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
    public ProfileStore Profiles { get; }
    public GuideStore Guide { get; }
    public TonightStore Tonight { get; }
    public WorkStore Works { get; }
    public LibraryStore Library { get; }
    public ListStore Lists { get; }
    /// <summary>«Registra la serie» (20/11/2026).</summary>
    public SeriesStore Series { get; }
    public NotificationStore Notifications { get; }
    public SubscriptionStore Subscriptions { get; }
    public PreferenceStore Preferences { get; }
    /// <summary>Note sulla conversione delle preferenze in liste dinamiche fatta all'avvio (per il registro).</summary>
    public IReadOnlyList<string> ConvertedPreferences { get; }
    public AlertStore Alerts { get; }
    /// <summary>Canali doppi uniti all'avvio (per il registro).</summary>
    public int MergedChannels { get; }
    public DoveLoFanno.Ratings.PlatformCatalog Platforms { get; }
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
        // canali doppi creati prima della chiave normalizzata (due playlist con tvg-id scritti diversi): si uniscono
        MergedChannels = Sources.MergeDuplicateChannels();
        Recordings = new RecordingStore(DbPath);
        Settings = new SettingsStore(DbPath);
        Resolver = new StreamResolver(Http);
        Prober = new StreamProber(Tools, Resolver);
        keepAwake = OperatingSystem.IsWindows() ? new WindowsKeepAwake() : NoKeepAwake.Instance;
        var recorder = new Recorder(Tools, Resolver, Clock, recorderOptions, keepAwake);
        Scheduler = new RecordingScheduler(Recordings, Sources, recorder, Http, Clock, new SchedulerOptions(RecordingsDir, LogDir));
        Backups = new Backup(DbPath, Path.Combine(DataDir, "backup"));
        Profiles = new ProfileStore(DbPath);
        Guide = new GuideStore(DbPath);
        Tonight = new TonightStore(DbPath, Guide);
        Works = new WorkStore(DbPath, Guide);
        Library = new LibraryStore(DbPath);
        Lists = new ListStore(DbPath, Guide);
        Series = new SeriesStore(DbPath, Guide, Recordings);
        Notifications = new NotificationStore(DbPath);
        Subscriptions = new SubscriptionStore(DbPath);
        Preferences = new PreferenceStore(DbPath, Guide);
        // le preferenze di prima («Cosa proporre») diventano liste dinamiche, una volta sola (19/11/2026)
        ConvertedPreferences = Preferences.ConvertToLists(Clock.GetUtcNow());
        Alerts = new AlertStore(DbPath, Guide, Subscriptions);
        Platforms = new DoveLoFanno.Ratings.PlatformCatalog(Path.Combine(DataDir, "ratings", "platforms-IT.json"));
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
