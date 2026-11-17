using DoveLoFanno.Data;
using DoveLoFanno.Media;
using DoveLoFanno.Ratings;

namespace DoveLoFanno.Library;

/// <summary>Le chiamate a TMDB che servono alla libreria (film e serie); il client vero è <see cref="TmdbClient"/>.</summary>
public interface ILibraryTmdb
{
    Task<IReadOnlyList<TmdbMovie>> SearchMovieAsync(string title, int year, CancellationToken ct);
    Task<IReadOnlyList<TmdbMovie>> SearchTvAsync(string title, int year, CancellationToken ct);
    Task<string> ImdbIdAsync(int tmdbId, CancellationToken ct);
    Task<string> TvImdbIdAsync(int tmdbId, CancellationToken ct);
    Task<MovieDetails> MovieDetailsAsync(int tmdbId, CancellationToken ct);
    Task<MovieDetails> TvDetailsAsync(int tmdbId, CancellationToken ct);
}

/// <summary>Come è andata una scansione.</summary>
public sealed record ScanReport(int Folders, int Files, int New, int Missing, int Probed, int Matched, int Unmatched, int Recordings, string Error);

/// <summary>
/// Scansione della libreria: registrazioni finite, file delle cartelle, lettura con ffprobe, abbinamento alle opere
/// (TMDB se c'è la chiave, altrimenti dal nome del file) e locandine. Un passo alla volta, senza fretta: un NAS con
/// migliaia di file la prima volta ci mette un po', poi si rileggono solo i file nuovi o cambiati.
/// </summary>
public sealed class LibraryScanner(LibraryStore store, WorkStore works, IMediaTools tools, TimeProvider clock)
{
    /// <summary>Pausa tra due chiamate a TMDB (il limite è circa 50 al secondo, si resta molto sotto).</summary>
    public TimeSpan Pace { get; init; } = TimeSpan.FromMilliseconds(120);

    /// <summary>Stato leggibile per le Impostazioni ("Leggo 120 file su 3.400"...).</summary>
    public string Status { get; private set; } = "";

    public async Task<ScanReport> ScanAsync(ILibraryTmdb? tmdb, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        Status = "Registrazioni";
        int recorded = store.SyncRecordings(now);

        int folders = 0, files = 0, fresh = 0, missing = 0;
        foreach (var f in store.Folders().Where(f => f.Enabled))
        {
            ct.ThrowIfCancellationRequested();
            folders++;
            Status = "Cartella " + f.Path;
            if (!Directory.Exists(f.Path))
            {
                store.FolderScanned(f.Id, f.Files, "cartella non trovata o disco non collegato", now);
                continue;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string error = "";
            int before = store.Counts().Files;
            try
            {
                foreach (var file in Walk(f.Path, ct))
                {
                    seen.Add(file.FullName);
                    store.Seen(f.Id, file.FullName, file.Length, file.LastWriteTimeUtc, MediaName.Parse(file.FullName), now);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = "lettura interrotta: " + ex.Message;
            }
            // con un errore di lettura non si segna niente come mancante: meglio un file in più che uno perso
            if (error.Length == 0) missing += store.MarkMissing(f.Id, seen, now);
            fresh += Math.Max(0, store.Counts().Files - before);
            files += seen.Count;
            store.FolderScanned(f.Id, seen.Count, error, now);
        }

        int probed = await ProbeAsync(ct);
        var (matched, unmatched, err) = await MatchAsync(tmdb, ct);
        if (tmdb != null && err.Length == 0) await DetailsAsync(tmdb, ct);
        Status = "";
        return new ScanReport(folders, files, fresh, missing, probed, matched, unmatched, recorded, err);
    }

    /// <summary>Solo registrazioni e lettura dei file nuovi: dopo ogni registrazione, senza rileggere le cartelle.</summary>
    public async Task<int> SyncRecordingsAsync(CancellationToken ct)
    {
        int n = store.SyncRecordings(clock.GetUtcNow());
        await ProbeAsync(ct);
        return n;
    }

    /// <summary>File video della cartella e delle sottocartelle, saltando extra, campioni e cartelle di sistema dei NAS.</summary>
    static IEnumerable<FileInfo> Walk(string root, CancellationToken ct)
    {
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(root));
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System, RecurseSubdirectories = false };
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            foreach (var sub in dir.EnumerateDirectories("*", options))
                if (!MediaName.SkipFolder(sub.Name)) stack.Push(sub);
            foreach (var file in dir.EnumerateFiles("*", options))
                if (MediaName.IsVideo(file.Name) && !MediaName.SkipFile(file.Name)) yield return file;
        }
    }

    async Task<int> ProbeAsync(CancellationToken ct)
    {
        var todo = store.ToProbe();
        int n = 0;
        foreach (var (id, path) in todo)
        {
            ct.ThrowIfCancellationRequested();
            Status = $"Leggo i file: {n + 1} di {todo.Count}";
            ProbeInfo? p = null;
            try { p = await MediaProbe.ProbeAsync(tools, path, ct); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            store.SetProbe(id, path, p);
            n++;
        }
        return n;
    }

    /// <summary>Abbina i file nuovi: (abbinati, non riconosciuti, errore di TMDB).</summary>
    async Task<(int Matched, int Unmatched, string Error)> MatchAsync(ILibraryTmdb? tmdb, CancellationToken ct)
    {
        var todo = store.Pending(tmdb != null);
        int matched = 0, unmatched = 0, n = 0;
        // più episodi della stessa serie: una sola ricerca
        var seriesCache = new Dictionary<string, long?>(StringComparer.Ordinal);
        foreach (var it in todo)
        {
            ct.ThrowIfCancellationRequested();
            Status = $"Riconosco i titoli: {++n} di {todo.Count}";
            var now = clock.GetUtcNow();
            if (it.TitleGuess.Length == 0) { store.SetMatch(it.Id, null, "unmatched"); unmatched++; continue; }
            bool episode = it.Season is not null && it.Episode is not null;
            try
            {
                long? work = episode || it.FolderKind == "series"
                    ? await SeriesAsync(tmdb, it, seriesCache, now, ct)
                    : await FilmAsync(tmdb, it, now, ct);
                if (work is long w) { store.SetMatch(it.Id, w, tmdb == null ? "local" : "matched"); matched++; }
                else { store.SetMatch(it.Id, null, "unmatched"); unmatched++; }
            }
            catch (TmdbKeyException ex) { return (matched, unmatched, ex.Message); }
            catch (HttpRequestException ex) { return (matched, unmatched, "TMDB non raggiungibile: " + ex.Message); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return (matched, unmatched, "TMDB non risponde"); }
            catch (System.Text.Json.JsonException) { return (matched, unmatched, "risposta di TMDB non leggibile"); }
        }
        return (matched, unmatched, "");
    }

    async Task<long?> FilmAsync(ILibraryTmdb? tmdb, PendingItem it, DateTimeOffset now, CancellationToken ct)
    {
        if (tmdb == null) return store.LocalWork(it.TitleGuess, it.YearGuess, "film", now);
        var best = await FindMovieAsync(tmdb, it.TitleGuess, it.YearGuess, ct);
        if (best == null) return null;
        string imdb = "";
        try { imdb = await tmdb.ImdbIdAsync(best.Id, ct); } catch (HttpRequestException) { }
        return store.FilmWork(best.Title, best.Year, best.Id, imdb, best.OriginalTitle, best.VoteAverage, best.VoteCount, best.IsDocumentary, now);
    }

    async Task<long?> SeriesAsync(ILibraryTmdb? tmdb, PendingItem it, Dictionary<string, long?> cache, DateTimeOffset now, CancellationToken ct)
    {
        string key = Epg.Titles.TitleKey(it.TitleGuess) + "|" + it.YearGuess;
        if (!cache.TryGetValue(key, out long? series))
        {
            if (tmdb == null) series = store.LocalWork(it.TitleGuess, it.YearGuess, "series", now);
            else
            {
                var best = await FindTvAsync(tmdb, it.TitleGuess, it.YearGuess, ct);
                if (best != null)
                {
                    string imdb = "";
                    try { imdb = await tmdb.TvImdbIdAsync(best.Id, ct); } catch (HttpRequestException) { }
                    series = store.SeriesWork(best.Title, best.Year, best.Id, imdb, best.OriginalTitle, best.VoteAverage, best.VoteCount, best.IsDocumentary, now);
                }
            }
            cache[key] = series;
        }
        if (series is not long s) return null;
        // un file in una cartella di serie senza stagione ed episodio resta della serie
        if (it.Season is int season && it.Episode is int ep) return store.EpisodeWork(s, season, ep, it.EpisodeTitle, now);
        return s;
    }

    /// <summary>Ricerca di un film come nella guida: con l'anno, poi senza; si accetta solo un titolo che corrisponde.</summary>
    public async Task<TmdbMovie?> FindMovieAsync(ILibraryTmdb tmdb, string title, int year, CancellationToken ct)
    {
        string q = TitleMatcher.QueryTitle(title);
        var tw = TitleMatcher.Words(q);
        if (tw.Count == 0) return null;
        var best = TitleMatcher.Pick(await tmdb.SearchMovieAsync(q, year, ct), tw, year);
        await Task.Delay(Pace, clock, ct);
        if (best == null && year > 0)
        {
            best = TitleMatcher.Pick(await tmdb.SearchMovieAsync(q, 0, ct), tw, year);
            await Task.Delay(Pace, clock, ct);
        }
        return best;
    }

    public async Task<TmdbMovie?> FindTvAsync(ILibraryTmdb tmdb, string title, int year, CancellationToken ct)
    {
        string q = TitleMatcher.QueryTitle(title);
        var tw = TitleMatcher.Words(q);
        if (tw.Count == 0) return null;
        var best = TitleMatcher.Pick(await tmdb.SearchTvAsync(q, 0, ct), tw, year);
        await Task.Delay(Pace, clock, ct);
        return best;
    }

    /// <summary>Locandine e trame delle opere della libreria che non le hanno ancora.</summary>
    async Task DetailsAsync(ILibraryTmdb tmdb, CancellationToken ct)
    {
        var todo = store.WorksWithoutDetails();
        int n = 0;
        foreach (var (work, id, kind) in todo)
        {
            ct.ThrowIfCancellationRequested();
            Status = $"Locandine: {++n} di {todo.Count}";
            try
            {
                var d = kind == "tv" ? await tmdb.TvDetailsAsync(id, ct) : await tmdb.MovieDetailsAsync(id, ct);
                works.SaveDetails(work, d, clock.GetUtcNow());
            }
            catch (Exception ex) when (ex is HttpRequestException or TmdbKeyException or System.Text.Json.JsonException) { return; }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return; }
            await Task.Delay(Pace, clock, ct);
        }
    }
}
