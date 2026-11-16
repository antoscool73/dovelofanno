using DoveLoFanno.Epg;

namespace DoveLoFanno.Ratings;

/// <summary>
/// Voti dei film della guida: TMDB trova il film da titolo italiano e anno e dà il codice IMDb; il voto IMDb
/// viene dal file ufficiale (riscaricato dopo 7 giorni). Cache su disco: ogni giorno si cercano solo i titoli nuovi.
/// Portato da RatingService di RegistraTV; il thread diventa <see cref="ProcessAsync"/>, che il server chiamerà
/// da un servizio in background.
/// </summary>
public sealed class RatingService
{
    readonly ITmdbSearch? tmdb;
    readonly HttpClient? http;
    readonly TimeProvider clock;
    readonly string cacheFile;
    readonly string imdbFile;
    readonly Lock sync = new();
    readonly RatingCache cache;
    readonly Queue<(string Key, string Title, int Year)> queue = new();
    readonly HashSet<string> queued = [];

    /// <param name="tmdb">null = nessuna chiave TMDB: niente ricerche, solo i voti già in cache.</param>
    /// <param name="http">per scaricare il file IMDb; null = solo il file già presente.</param>
    public RatingService(ITmdbSearch? tmdb, string dir, HttpClient? http = null, TimeProvider? clock = null)
    {
        this.tmdb = tmdb;
        this.http = http;
        this.clock = clock ?? TimeProvider.System;
        Directory.CreateDirectory(dir);
        cacheFile = Path.Combine(dir, "cache.tsv");
        imdbFile = Path.Combine(dir, "title.ratings.tsv.gz");
        cache = RatingCache.Load(cacheFile);
    }

    /// <summary>Cresce a ogni cambiamento dei voti, per chi deve ricalcolare (proposte, filtri).</summary>
    public int Version { get; private set; }
    public string Status { get; private set; } = "";
    public string Error { get; private set; } = "";

    /// <summary>
    /// Voti IMDb dal file ufficiale. Facoltativi e spenti di base nel server: la licenza del file è solo personale e non
    /// commerciale. Spenti, si mostra il voto TMDB (con almeno 20 voti).
    /// </summary>
    public bool UseImdb { get; init; } = true;

    /// <summary>Delay tra due ricerche, per non pesare su TMDB (RegistraTV: 120 ms).</summary>
    public TimeSpan Pace { get; init; } = TimeSpan.FromMilliseconds(120);

    DateTime Now => clock.GetLocalNow().DateTime;

    public int Pending { get { lock (sync) return queue.Count; } }

    /// <summary>Film cercati e trovati nella cache.</summary>
    public (int Searched, int Found) Counts { get { lock (sync) return (cache.Entries.Count, cache.Entries.Values.Count(e => e.Found)); } }

    public RatingInfo? Lookup(EpgProgramme p)
    {
        lock (sync) return cache.Lookup(p.Title, p.Year, UseImdb);
    }

    public double RatingOf(EpgProgramme p) => Lookup(p)?.Value ?? 0;

    /// <summary>"trovato", "" = cercato e non trovato, null = non ancora cercato.</summary>
    public string? State(EpgProgramme p)
    {
        lock (sync)
            return cache.Entries.TryGetValue(RatingCache.KeyOf(p.Title, p.Year), out var e) ? (e.Found ? "trovato" : "") : null;
    }

    /// <summary>Mette in coda i film da cercare; restituisce quanti sono stati aggiunti.</summary>
    public int Request(IEnumerable<EpgProgramme> films)
    {
        int added = 0;
        var now = Now;
        lock (sync)
        {
            foreach (var p in films)
            {
                string k = RatingCache.KeyOf(p.Title, p.Year);
                if (queued.Contains(k) || !cache.NeedsSearch(k, now)) continue;
                queue.Enqueue((k, p.Title, p.Year));
                queued.Add(k);
                added++;
            }
        }
        return added;
    }

    /// <summary>Cerca i film in coda, poi aggiorna i voti IMDb e salva la cache.</summary>
    public async Task ProcessAsync(CancellationToken ct = default)
    {
        try
        {
            int done = 0;
            while (tmdb != null)
            {
                (string Key, string Title, int Year) r;
                lock (sync)
                {
                    if (queue.Count == 0) break;
                    r = queue.Peek();
                }
                Status = $"Ricerca dei voti: {Pending} film da cercare";
                var res = await TitleMatcher.ResolveAsync(tmdb, r.Title, r.Year, ct);
                lock (sync)
                {
                    queue.Dequeue();
                    queued.Remove(r.Key);
                    cache.Entries[r.Key] = RatingEntry.From(res, Now);
                }
                if (++done % 10 == 0) { Save(); Version++; }
                if (Pace > TimeSpan.Zero) await Task.Delay(Pace, clock, ct);
            }
            Error = "";
            if (UseImdb) await UpdateImdbAsync(ct);
        }
        catch (Exception ex) when (ex is TmdbKeyException or HttpRequestException or IOException or InvalidDataException)
        {
            Error = ex.Message;
        }
        Save();
        Version++;
        int found;
        lock (sync) found = cache.Entries.Values.Count(e => e.Found);
        Status = tmdb == null ? "Manca la chiave TMDB (Impostazioni)" : $"Voti pronti: {found} film trovati";
    }

    void Save()
    {
        lock (sync) cache.Save(cacheFile, Now);
    }

    async Task UpdateImdbAsync(CancellationToken ct)
    {
        DateTime fileTime = File.Exists(imdbFile) ? File.GetLastWriteTime(imdbFile) : DateTime.MinValue;
        bool old = (Now - fileTime).TotalDays >= 7;
        HashSet<string> need;
        lock (sync)
            need = cache.Entries.Values
                .Where(e => e.Found && e.ImdbId.Length > 0 && (e.ImdbChecked < fileTime || old))
                .Select(e => e.ImdbId).ToHashSet();
        if (need.Count == 0) return;
        if (old && http != null)
        {
            Status = "Scaricamento dei voti IMDb...";
            string tmp = imdbFile + ".tmp";
            try
            {
                using (var resp = await http.GetAsync(ImdbRatings.Url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    await using var f = File.Create(tmp);
                    await resp.Content.CopyToAsync(f, ct);
                }
                File.Move(tmp, imdbFile, overwrite: true);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                try { File.Delete(tmp); } catch (IOException) { }
                if (!File.Exists(imdbFile)) { Error = "voti IMDb non scaricati: " + ex.Message; return; }
            }
            fileTime = File.GetLastWriteTime(imdbFile);
        }
        if (!File.Exists(imdbFile)) return;
        Status = "Lettura dei voti IMDb...";
        var found = ImdbRatings.Read(imdbFile, need);
        lock (sync)
        {
            foreach (var e in cache.Entries.Values)
            {
                if (!e.Found || e.ImdbId.Length == 0 || !need.Contains(e.ImdbId)) continue;
                if (found.TryGetValue(e.ImdbId, out var r)) { e.ImdbVote = r.Rating; e.ImdbVotes = r.Votes; }
                e.ImdbChecked = fileTime;
            }
        }
    }
}
