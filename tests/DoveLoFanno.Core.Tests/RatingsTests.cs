using System.IO.Compression;
using System.Text;
using DoveLoFanno.Epg;
using DoveLoFanno.Ratings;

namespace DoveLoFanno.Tests;

public class RatingsTests
{
    static TmdbMovie M(int id, string title, string year, int votes, string? original = null, double avg = 7) =>
        new(id, title, original ?? title, year.Length > 0 ? year + "-01-01" : "", avg, votes);

    [Theory]
    [InlineData("Io e Marley", "Io & Marley")]
    [InlineData("White House Down - Sotto Assedio", "Sotto assedio - White House down")]
    [InlineData("L' Amica geniale", "L'amica geniale")]
    public void StessoTitolo(string a, string b) =>
        Assert.Equal(2, TitleMatcher.Match(TitleMatcher.Words(a), TitleMatcher.Words(b)));

    [Fact]
    public void TitoloContenuto_TitoliMediasetConOriginaleEItaliano()
    {
        var tw = TitleMatcher.Words("City Of Angels Citta' Degli Angeli");
        var m = M(1, "City of Angels - La città degli angeli", "1998", 1500, "City of Angels");
        Assert.Equal(1, TitleMatcher.TitleMatch(m with { Title = "Città degli angeli" }, tw));
        // contenuto: accettato solo con l'anno giusto ±1 o senza anno nella guida
        Assert.NotNull(TitleMatcher.Pick([m with { Title = "Città degli angeli" }], tw, 1998));
        Assert.NotNull(TitleMatcher.Pick([m with { Title = "Città degli angeli" }], tw, 0));
        Assert.Null(TitleMatcher.Pick([m with { Title = "Città degli angeli" }], tw, 2005));
    }

    [Fact]
    public void FilmNotoBatteOmonimoSconosciuto()
    {
        var tw = TitleMatcher.Words("Chase");
        var best = TitleMatcher.Pick([M(1, "Chase", "2022", 1), M(2, "Chase - Scomparsa", "2022", 1127, "Chase")], tw, 2022);
        Assert.Equal(2, best?.Id);
    }

    [Fact]
    public async Task AnnoSbagliato_SiRiprovaSenzaAnno()
    {
        var tmdb = new RatingsOracleTests.RecordedTmdb([], [M(9294, "Phenomenon", "1996", 1200)], 2019, _ => "tt0117333");
        var r = await TitleMatcher.ResolveAsync(tmdb, "Phenomenon (film)", 2019);
        Assert.Equal((9294, "tt0117333"), (r.Movie?.Id, r.ImdbId));
        Assert.Equal(new[] { 2019, 0 }, tmdb.Searches);
    }

    static string Line(string key, string found, string checkedAt, int tmdbCount = 0, string imdbVote = "0", string? ver = "2") =>
        string.Join('\t', new[] { key, found, "10", "tt1", "Orig", "1996", "6.5", tmdbCount.ToString(), imdbVote, "100", checkedAt, "000101010000", ver }.Where(x => x != null));

    [Fact]
    public void Cache_FormatoDiRegistraTV()
    {
        string line = "tigre e ancora viva sandokan alla riscossa|0\t1\t104246\ttt0076823\tLa tigre è ancora viva: Sandokan alla riscossa!\t1977\t6.1\t29\t6.4\t358\t202611131541\t202611131542\t1";
        Assert.True(RatingCache.TryParse(line, out var key, out var e));
        Assert.Equal(("tigre e ancora viva sandokan alla riscossa|0", 104246, 6.4, 1), (key, e.TmdbId, e.ImdbVote, e.Ver));
        Assert.Equal(line, RatingCache.Format(key, e));
        // 12 colonne = versione 1 delle regole
        Assert.True(RatingCache.TryParse(string.Join('\t', line.Split('\t').Take(12)), out _, out var old));
        Assert.Equal(1, old.Ver);
        Assert.False(RatingCache.TryParse("rotta\t1\tx", out _, out _));
    }

    [Fact]
    public void Cache_QuandoRicercare()
    {
        var now = new DateTime(2026, 11, 16, 12, 0, 0);
        var c = new RatingCache();
        void Add(string l) { RatingCache.TryParse(l, out var k, out var e); c.Entries[k] = e; }
        Add(Line("trovato|1996", "1", "202611010000", imdbVote: "7.1"));
        Add(Line("nontrovato recente|0", "0", "202611100000"));
        Add(Line("nontrovato vecchio|0", "0", "202610010000"));
        Add(Line("senzavoto v1|2019", "1", "202611130000", tmdbCount: 0, ver: "1"));
        Add(Line("senzavoto v2|2019", "1", "202611140000", tmdbCount: 0));
        Assert.False(c.NeedsSearch("trovato|1996", now));
        Assert.False(c.NeedsSearch("nontrovato recente|0", now));
        Assert.True(c.NeedsSearch("nontrovato vecchio|0", now));        // dopo 30 giorni
        Assert.True(c.NeedsSearch("senzavoto v1|2019", now));           // regole più vecchie
        Assert.False(c.NeedsSearch("senzavoto v2|2019", now));
        Assert.True(c.NeedsSearch("mai visto|0", now));
        Assert.False(c.NeedsSearch("|0", now));                         // titolo vuoto
    }

    [Fact]
    public void Cache_VotoMostrato()
    {
        var c = new RatingCache();
        c.Entries["padrino|1972"] = new RatingEntry { Found = true, ImdbVote = 9.2, ImdbVotes = 2_000_000, TmdbVote = 8.7, TmdbCount = 20000 };
        c.Entries["raro|2001"] = new RatingEntry { Found = true, TmdbVote = 6, TmdbCount = 19 };
        c.Entries["tmdb|2001"] = new RatingEntry { Found = true, TmdbVote = 6.4, TmdbCount = 20 };
        Assert.Equal(("IMDb", 9.2), (c.Lookup("Il Padrino", 1972)?.Source, c.Lookup("Il Padrino", 1972)?.Value));
        Assert.Null(c.Lookup("Raro", 2001));                            // meno di 20 voti TMDB
        Assert.Equal("TMDB", c.Lookup("TMDB", 2001)?.Source);
    }

    static MemoryStream Gzip(string text)
    {
        var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(Encoding.UTF8.GetBytes(text));
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void FileImdb_SoloICodiciCheServono()
    {
        using var gz = Gzip("tconst\taverageRating\tnumVotes\ntt0111161\t9.3\t3000000\ntt0068646\t9.2\t2100000\n");
        var r = ImdbRatings.Read(gz, new HashSet<string> { "tt0111161", "tt9999999" });
        Assert.Single(r);
        Assert.Equal((9.3, 3000000), r["tt0111161"]);
    }

    sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public async Task Servizio_CercaSalvaERileggeLaCache()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dlf-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new FixedClock(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
            // file IMDb già presente e recente: non si scarica nulla
            Directory.CreateDirectory(dir);
            string imdb = Path.Combine(dir, "title.ratings.tsv.gz");
            using (var f = File.Create(imdb)) Gzip("tconst\taverageRating\tnumVotes\ntt0117333\t6.4\t140000\n").CopyTo(f);
            File.SetLastWriteTime(imdb, new DateTime(2026, 11, 15));

            var tmdb = new RatingsOracleTests.RecordedTmdb([M(9294, "Phenomenon", "1996", 1200)], [], 1996, _ => "tt0117333");
            var svc = new RatingService(tmdb, dir, clock: clock) { Pace = TimeSpan.Zero };
            var film = new EpgProgramme { Title = "Phenomenon", Year = 1996, Group = Genres.Film };
            Assert.Null(svc.State(film));
            Assert.Equal(1, svc.Request([film, film]));
            await svc.ProcessAsync();
            Assert.Equal("", svc.Error);
            Assert.Equal(("IMDb", 6.4), (svc.Lookup(film)?.Source, svc.Lookup(film)?.Value));

            // nuova istanza senza chiave: legge la cache e non ricerca
            var again = new RatingService(null, dir, clock: clock);
            Assert.Equal(6.4, again.RatingOf(film));
            Assert.Equal(0, again.Request([film]));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
