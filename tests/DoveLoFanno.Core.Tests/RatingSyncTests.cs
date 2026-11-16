using System.IO.Compression;
using System.Text;
using DoveLoFanno.Data;
using DoveLoFanno.Epg;
using DoveLoFanno.Ratings;
using DoveLoFanno.Sources;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

public sealed class RatingSyncTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-rsync-" + Guid.NewGuid().ToString("N"));
    string Db => Path.Combine(dir, "dlf.db");
    static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    GuideStore Guide()
    {
        var src = new SourceStore(Db);
        long s = src.AddSource("Prova", "https://example.org/x.m3u", Now);
        src.Import(s, M3uParser.Parse("#EXTM3U\n#EXTINF:-1 tvg-id=\"Canale5.it\",Canale 5\nhttp://x/c5.m3u8\n"), Now);
        var g = new GuideStore(Db);
        long gid = g.AddSource("https://example.org/guida.xml.gz");
        g.Save(gid, XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(GuideStoreTests.Xmltv(Now))), Now.LocalDateTime), Now);
        return g;
    }

    static TmdbMovie Padrino => new(238, "Il padrino", "The Godfather", "1972-03-14", 8.7, 21000);

    [Fact]
    public async Task VotiNelleOpereDellaGuida()
    {
        var guide = Guide();
        Assert.Equal(2, guide.FilmWorks(Now).Count);                       // Il Padrino e Città aperta
        var tmdb = new RatingsOracleTests.RecordedTmdb([Padrino], [], 1972, _ => "tt0068646");
        var svc = new RatingService(tmdb, Path.Combine(dir, "ratings"), clock: new FixedClock(Now)) { Pace = TimeSpan.Zero, UseImdb = false };

        Assert.Equal(1, await RatingSync.RunAsync(guide, svc, Now));
        var film = guide.Query(new GuideQuery(Now, Now.AddHours(6))).Single(p => p.Title == "Il Padrino");
        Assert.Equal((8.7, "TMDB"), (film.Rating, film.RatingSource));
        Assert.Single(guide.Query(new GuideQuery(Now, Now.AddHours(6)) { MinRating = 8 }));
        Assert.Null(guide.Query(new GuideQuery(Now, Now.AddHours(6))).Single(p => p.Title == "Città aperta").Rating);

        // secondo giro: tutto dalla cache, anche "Città aperta" non trovato (si riprova dopo 30 giorni)
        int searches = tmdb.Searches.Count;
        await RatingSync.RunAsync(guide, svc, Now);
        Assert.Equal(searches, tmdb.Searches.Count);
    }

    [Fact]
    public async Task ConImdbAttivoVinceIlVotoImdb()
    {
        var guide = Guide();
        string rdir = Path.Combine(dir, "ratings");
        Directory.CreateDirectory(rdir);
        string imdb = Path.Combine(rdir, "title.ratings.tsv.gz");
        using (var f = File.Create(imdb))
        using (var gz = new GZipStream(f, CompressionLevel.Fastest))
            gz.Write(Encoding.UTF8.GetBytes("tconst\taverageRating\tnumVotes\ntt0068646\t9.2\t2100000\n"));
        File.SetLastWriteTime(imdb, DateTime.Now);
        var tmdb = new RatingsOracleTests.RecordedTmdb([Padrino], [], 1972, _ => "tt0068646");
        var svc = new RatingService(tmdb, rdir, clock: new FixedClock(Now)) { Pace = TimeSpan.Zero, UseImdb = true };
        await RatingSync.RunAsync(guide, svc, Now);
        var film = guide.Query(new GuideQuery(Now, Now.AddHours(6))).Single(p => p.Title == "Il Padrino");
        Assert.Equal((9.2, "IMDb"), (film.Rating, film.RatingSource));
    }
}
