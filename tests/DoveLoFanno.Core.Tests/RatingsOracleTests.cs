using System.Text.Json;
using DoveLoFanno.Ratings;
using Xunit.Abstractions;

namespace DoveLoFanno.Tests;

/// <summary>
/// Oracolo dei voti: per ogni film della guida registrata (Fixtures/tmdb-oracle.json) il nuovo abbinamento deve
/// scegliere lo stesso film TMDB di RegistraTV, con le stesse risposte di TMDB.
/// </summary>
public class RatingsOracleTests(ITestOutputHelper output)
{
    sealed record Case(string Title, int Year, List<TmdbMovie> WithYear, List<TmdbMovie> WithoutYear, bool Found, int TmdbId, string ImdbId);

    /// <summary>TMDB finto: risponde con le risposte registrate e conta le ricerche fatte.</summary>
    internal sealed class RecordedTmdb(IReadOnlyList<TmdbMovie> withYear, IReadOnlyList<TmdbMovie> withoutYear, int year, Func<int, string> imdb) : ITmdbSearch
    {
        public List<int> Searches { get; } = [];

        public Task<IReadOnlyList<TmdbMovie>> SearchMovieAsync(string title, int y, CancellationToken ct)
        {
            Searches.Add(y);
            return Task.FromResult(y > 0 || year == 0 ? withYear : withoutYear);
        }

        public Task<string> ImdbIdAsync(int tmdbId, CancellationToken ct) => Task.FromResult(imdb(tmdbId));
    }

    static List<Case> LoadCases()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "tmdb-oracle.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var cases = new List<Case>();
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            static List<TmdbMovie> Movies(JsonElement a) =>
                a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(TmdbClient.Parse).ToList() : [];
            cases.Add(new Case(
                c.GetProperty("title").GetString() ?? "", c.GetProperty("year").GetInt32(),
                Movies(c.GetProperty("withYear")), Movies(c.GetProperty("withoutYear")),
                c.GetProperty("found").GetBoolean(), c.GetProperty("tmdbId").GetInt32(), c.GetProperty("imdbId").GetString() ?? ""));
        }
        return cases;
    }

    [Fact]
    public async Task StessiFilmScelti()
    {
        var cases = LoadCases();
        var diffs = new List<string>();
        foreach (var c in cases)
        {
            var tmdb = new RecordedTmdb(c.WithYear, c.WithoutYear, c.Year, id => id == c.TmdbId ? c.ImdbId : "");
            var r = await TitleMatcher.ResolveAsync(tmdb, c.Title, c.Year);
            int got = r.Movie?.Id ?? 0;
            int want = c.Found ? c.TmdbId : 0;
            if (got != want || (r.Found && r.ImdbId != c.ImdbId))
                diffs.Add($"{c.Title} ({c.Year}): RegistraTV {want} {c.ImdbId}, nuovo {got} {r.ImdbId}");
        }
        output.WriteLine($"Film: {cases.Count}, trovati da RegistraTV: {cases.Count(c => c.Found)}, differenze: {diffs.Count}");
        foreach (var d in diffs.Take(20)) output.WriteLine(d);
        Assert.True(cases.Count > 100, "oracolo con troppo pochi film");
        Assert.Empty(diffs);
    }

    [Theory]
    [InlineData("Phenomenon", 2019, 9294)]     // l'omonimo del 2019 ha 0 voti: vince il film del 1996
    [InlineData("Chase", 2022, 961484)]        // "Chase - Scomparsa", non un "Chase" da 1 voto
    public async Task CasiDelCollaudoDel14Novembre(string title, int year, int expectedTmdbId)
    {
        var c = LoadCases().Single(x => x.Title == title && x.Year == year);
        var r = await TitleMatcher.ResolveAsync(new RecordedTmdb(c.WithYear, c.WithoutYear, c.Year, _ => ""), c.Title, c.Year);
        Assert.Equal(expectedTmdbId, r.Movie?.Id);
    }
}
