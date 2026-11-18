using DoveLoFanno.Data;

namespace DoveLoFanno.Ratings;

/// <summary>Voto TMDB e codice IMDb di un film (movie) o di una serie (tv).</summary>
public interface ITmdbVotes
{
    Task<(double Average, int Count)> VotesAsync(int tmdbId, string kind, CancellationToken ct);
    Task<string> ImdbIdAsync(int tmdbId, string kind, CancellationToken ct);
}

/// <summary>
/// Voti dei titoli nelle liste (la mia serata compresa) che non passano in guida né sono in libreria, per esempio un
/// originale Prime Video trovato con la lente: il voto preso alla creazione della scheda si aggiorna. Prima il codice IMDb
/// (se manca), poi il voto IMDb dal file già scaricato (se i voti IMDb sono attivi), altrimenti il voto TMDB da 20 voti in su.
/// </summary>
public static class ListRatingSync
{
    public const int MinTmdbVotes = 20;

    /// <returns>opere con un voto nuovo o aggiornato</returns>
    public static async Task<int> RunAsync(WorkStore works, ITmdbVotes? tmdb, string? imdbFile, CancellationToken ct, int max = 200, TimeSpan? pace = null)
    {
        var need = works.ListWorksWithoutImdbRating(max);
        if (need.Count == 0) return 0;
        var ids = new Dictionary<long, string>();
        foreach (var w in need)
        {
            string imdb = w.ImdbId;
            if (imdb.Length == 0 && tmdb != null)
            {
                try { imdb = await tmdb.ImdbIdAsync((int)w.TmdbId, w.Kind, ct); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested) { }
                if (imdb.Length > 0) works.SetImdbId(w.WorkId, imdb);
                if (pace is { } p1) await Task.Delay(p1, ct);
            }
            if (imdb.Length > 0) ids[w.WorkId] = imdb;
        }

        int n = 0;
        var imdbRated = new HashSet<long>();
        if (imdbFile != null && File.Exists(imdbFile) && ids.Count > 0)
        {
            var found = ImdbRatings.Read(imdbFile, ids.Values.ToHashSet());
            foreach (var (work, imdb) in ids)
                if (found.TryGetValue(imdb, out var r) && r.Votes > 0)
                {
                    works.SetImdbRating(work, r.Rating, r.Votes);
                    imdbRated.Add(work);
                    n++;
                }
        }
        if (tmdb == null) return n;
        foreach (var w in need.Where(x => !imdbRated.Contains(x.WorkId)))
        {
            try
            {
                var (avg, count) = await tmdb.VotesAsync((int)w.TmdbId, w.Kind, ct);
                if (count >= MinTmdbVotes && avg > 0 && works.SetTmdbRating(w.WorkId, avg, count)) n++;
            }
            catch (TmdbKeyException) { break; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested) { }
            if (pace is { } p2) await Task.Delay(p2, ct);
        }
        return n;
    }
}
