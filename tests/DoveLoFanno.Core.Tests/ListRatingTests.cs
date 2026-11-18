using System.IO.Compression;
using System.Text;
using DoveLoFanno.Data;
using DoveLoFanno.Ratings;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

/// <summary>Voti dei titoli nelle liste che non passano in guida (es. un originale trovato con la lente).</summary>
public sealed class ListRatingTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-lrat-" + Guid.NewGuid().ToString("N"));
    string Db => Path.Combine(dir, "dlf.db");
    static readonly DateTimeOffset Now = new(2026, 11, 18, 20, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    sealed class FakeTmdb(Dictionary<int, (double, int)> votes, Dictionary<int, string> imdb) : ITmdbVotes
    {
        public int Calls;
        public Task<(double Average, int Count)> VotesAsync(int tmdbId, string kind, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(votes.TryGetValue(tmdbId, out var v) ? v : (0.0, 0));
        }
        public Task<string> ImdbIdAsync(int tmdbId, string kind, CancellationToken ct) =>
            Task.FromResult(imdb.TryGetValue(tmdbId, out var s) ? s : "");
    }

    [Fact]
    public async Task VotoImdbDalFile_AltrimentiTmdbDa20Voti()
    {
        Directory.CreateDirectory(dir);
        var guide = new GuideStore(Db);
        var works = new WorkStore(Db, guide);
        var lib = new LibraryStore(Db);
        var tonight = new TonightStore(Db, guide);
        long a = new ProfileStore(Db).Create("Anna", false, null, null, Now).Id;

        long madden = lib.FilmWork("Madden", 2026, 1131943, "", "Madden", 7.2, 4, false, Now);     // 4 voti: nessun voto
        long dune = lib.FilmWork("Dune - Parte due", 2024, 693134, "", "", 8.1, 7000, false, Now);  // voto TMDB già preso
        long fuori = lib.FilmWork("Fuori lista", 2020, 42, "", "", 0, 0, false, Now);               // non in lista: ignorato
        tonight.Add(a, madden, Now);
        tonight.Add(a, dune, Now);

        string file = Path.Combine(dir, "title.ratings.tsv.gz");
        await using (var fs = File.Create(file))
        await using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
            await gz.WriteAsync(Encoding.UTF8.GetBytes("tconst\taverageRating\tnumVotes\ntt15239678\t8.5\t600000\n"));

        var tmdb = new FakeTmdb(new() { [1131943] = (7.0, 35) }, new() { [1131943] = "tt24818230", [693134] = "tt15239678" });
        int n = await ListRatingSync.RunAsync(works, tmdb, file, CancellationToken.None);
        Assert.Equal(2, n);

        var d = works.Detail(dune, a, Now)!;
        Assert.Equal((8.5, "IMDb", "tt15239678"), (d.Rating!.Value, d.RatingSource, d.ImdbId));   // dal file IMDb
        var m = works.Detail(madden, a, Now)!;
        Assert.Equal((7.0, "TMDB", "tt24818230"), (m.Rating!.Value, m.RatingSource, m.ImdbId));  // non ancora su IMDb: TMDB, 35 voti
        Assert.Null(works.Detail(fuori, a, Now)!.Rating);
        Assert.Equal(1, tmdb.Calls);                                                              // Dune non chiede TMDB

        // al giro dopo Dune ha già il voto IMDb e non si ricontrolla; Madden sotto i 20 voti resta com'era
        var few = new FakeTmdb(new() { [1131943] = (6.0, 10) }, []);
        Assert.Equal(0, await ListRatingSync.RunAsync(works, few, null, CancellationToken.None));
        Assert.Equal(7.0, works.Detail(madden, a, Now)!.Rating);
    }
}
