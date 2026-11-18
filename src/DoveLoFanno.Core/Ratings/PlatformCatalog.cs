using System.Text.Json;

namespace DoveLoFanno.Ratings;

/// <summary>Una piattaforma come la dà TMDB: nome, logo (/abc.png) e rilievo in Italia (0 = la più diffusa).</summary>
public sealed record PlatformEntry(string Name, string LogoPath, int Priority);

/// <summary>
/// Piattaforma per l'interfaccia: logo (URL completo o ""), ordine di rilievo, gruppo e nome breve. I canali venduti dentro
/// un'altra piattaforma (es. "Cine Comico Amazon Channel", abbonamento aggiuntivo dentro Prime Video) stanno nel gruppo di
/// quella piattaforma con il nome breve "Cine Comico"; le versioni "with Ads" nel gruppo della piattaforma.
/// </summary>
public sealed record Platform(string Name, string Logo, int Priority, string Group, string Short);

/// <summary>
/// Catalogo delle piattaforme in Italia da TMDB (watch/providers), tenuto in un file e riletto ogni 7 giorni. Senza
/// chiave TMDB o senza rete restano i nomi senza logo, nell'ordine delle piattaforme più comuni.
/// </summary>
public sealed class PlatformCatalog(string file)
{
    public const string Region = "IT";
    const string LogoBase = "https://image.tmdb.org/t/p/w92";
    static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    /// <summary>Ordine di riserva senza catalogo: le più diffuse in Italia.</summary>
    static readonly string[] Fallback =
    [
        "Netflix", "Amazon Prime Video", "Disney Plus", "Now TV", "Sky Go", "Apple TV", "Paramount Plus", "Timvision",
        "Rai Play", "Mediaset Infinity", "Discovery+", "DAZN", "HBO Max", "MUBI", "Crunchyroll", "Rakuten TV", "Pluto TV",
    ];

    // suffisso del nome → piattaforma che vende il canale
    static readonly (string Suffix, string Group)[] Channels =
    [
        (" Amazon Channel", "Amazon Prime Video"),
        (" Apple TV Channel", "Apple TV"),
        (" Roku Premium Channel", "Roku"),
    ];

    readonly object gate = new();
    readonly SemaphoreSlim refresh = new(1, 1);
    Dictionary<string, PlatformEntry>? entries;
    DateTimeOffset fetched;

    /// <summary>Gruppo e nome breve: "Cine Comico Amazon Channel" → ("Amazon Prime Video", "Cine Comico"); "Netflix basic with Ads" → ("Netflix", ...).</summary>
    public static (string Group, string Short) Classify(string name)
    {
        string n = name.Trim();
        foreach (var (suffix, group) in Channels)
            if (n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return (group, n[..^suffix.Length].Trim());
        foreach (string ads in new[] { " basic with Ads", " Standard with Ads", " with Ads" })
            if (n.EndsWith(ads, StringComparison.OrdinalIgnoreCase))
            {
                string b = n[..^ads.Length].Trim();
                return (b, n);
            }
        return (n, n);
    }

    /// <summary>Le piattaforme del catalogo più i nomi indicati, dalla più diffusa; i canali dopo la loro piattaforma.</summary>
    public List<Platform> All(IEnumerable<string> extra)
    {
        var map = Load();
        var names = new HashSet<string>(map.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (string e in extra) if (!string.IsNullOrWhiteSpace(e)) names.Add(e.Trim());
        int Prio(string n)
        {
            if (map.TryGetValue(n, out var p)) return p.Priority;
            int i = Array.FindIndex(Fallback, f => f.Equals(n, StringComparison.OrdinalIgnoreCase));
            return i >= 0 ? i : 1000;
        }
        return names.Select(n =>
        {
            var (group, shortName) = Classify(n);
            string logo = map.TryGetValue(n, out var p) && p.LogoPath.Length > 0 ? LogoBase + p.LogoPath : "";
            return new Platform(n, logo, Prio(n), group, shortName);
        })
            .OrderBy(p => Prio(p.Group)).ThenBy(p => p.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name.Equals(p.Group, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Priority).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool Stale(DateTimeOffset now) { Load(); return now - fetched > MaxAge; }

    /// <summary>Rilegge da TMDB; in caso di errore resta il catalogo che c'era.</summary>
    public async Task RefreshAsync(TmdbClient tmdb, DateTimeOffset now, CancellationToken ct)
    {
        if (!await refresh.WaitAsync(0, ct)) return;
        try
        {
            var list = await tmdb.WatchProvidersAsync(Region, ct);
            if (list.Count == 0) return;
            var map = list.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string tmp = file + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(new CacheFile(now, [.. list.OrderBy(x => x.Priority)])), ct);
            File.Move(tmp, file, true);
            lock (gate) { entries = map; fetched = now; }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TmdbKeyException or IOException or JsonException) { }
        finally { refresh.Release(); }
    }

    Dictionary<string, PlatformEntry> Load()
    {
        lock (gate)
        {
            if (entries != null) return entries;
            entries = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(file) && JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(file)) is { } c)
                {
                    foreach (var e in c.Items) entries[e.Name] = e;
                    fetched = c.Fetched;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
            return entries;
        }
    }

    sealed record CacheFile(DateTimeOffset Fetched, List<PlatformEntry> Items);
}
