using DoveLoFanno.Data;
using DoveLoFanno.Epg;

namespace DoveLoFanno.Ratings;

/// <summary>
/// Voti dei film della guida: mette in coda le opere film con un passaggio futuro, cerca quelle nuove su TMDB
/// (con la cache, ogni giorno solo i titoli nuovi) e scrive il voto nell'opera, dove lo leggono guida, filtri e proposte.
/// </summary>
public static class RatingSync
{
    /// <summary>Un giro completo; restituisce quante opere hanno un voto.</summary>
    public static async Task<int> RunAsync(GuideStore guide, RatingService ratings, DateTimeOffset now, CancellationToken ct = default)
    {
        var films = guide.FilmWorks(now);
        ratings.Request(films.Select(f => new EpgProgramme { Title = f.Title, Year = f.Year, Group = Genres.Film }));
        await ratings.ProcessAsync(ct);
        int rated = 0;
        foreach (var f in films)
        {
            var info = ratings.Lookup(new EpgProgramme { Title = f.Title, Year = f.Year, Group = Genres.Film });
            guide.SetRating(f.WorkId, info, now);
            if (info != null) rated++;
        }
        return rated;
    }
}
