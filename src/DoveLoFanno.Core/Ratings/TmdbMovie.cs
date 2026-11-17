namespace DoveLoFanno.Ratings;

/// <summary>Un risultato di TMDB search/movie, con i soli campi che servono all'abbinamento.</summary>
public sealed record TmdbMovie(int Id, string Title, string OriginalTitle, string ReleaseDate, double VoteAverage, int VoteCount)
{
    public int Year => ReleaseDate.Length >= 4 && int.TryParse(ReleaseDate.AsSpan(0, 4), out int y) ? y : 0;

    /// <summary>Generi TMDB (99 = documentario); vuoto nelle risposte registrate dei test.</summary>
    public IReadOnlyList<int> GenreIds { get; init; } = [];

    public bool IsDocumentary => GenreIds.Contains(99);
}

/// <summary>Accesso a TMDB; il servizio reale è <see cref="TmdbClient"/>, i test usano risposte registrate.</summary>
public interface ITmdbSearch
{
    /// <summary>search/movie in italiano; <paramref name="year"/> 0 = senza anno.</summary>
    Task<IReadOnlyList<TmdbMovie>> SearchMovieAsync(string title, int year, CancellationToken ct);

    /// <summary>Codice IMDb (tt...) da movie/{id}/external_ids, "" se manca.</summary>
    Task<string> ImdbIdAsync(int tmdbId, CancellationToken ct);
}

/// <summary>Chiave TMDB rifiutata (HTTP 401).</summary>
public sealed class TmdbKeyException() : Exception("chiave TMDB non valida");
