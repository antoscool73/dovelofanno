using DoveLoFanno.Sources;

namespace DoveLoFanno.Tests;

/// <summary>Numeri del digitale terrestre (LCN) per i canali della playlist e della guida.</summary>
public sealed class ChannelNumbersTests
{
    [Theory]
    [InlineData("id:rai1.it", "Rai 1", 1)]
    [InlineData("id:canale5.it", "Canale 5", 5)]
    [InlineData("id:tv8.it", "TV8", 8)]
    [InlineData("id:mediaset20.it", "20 Mediaset", 20)]
    [InlineData("id:mediaset27twentyseven.it", "Twenty Seven", 27)]
    [InlineData("id:la7d.it", "LA7 Cinema", 29)]
    [InlineData("id:rtl102.5tv.it", "RTL 102.5", 36)]
    [InlineData("id:super.it", "Super!", 47)]
    [InlineData("id:hgtv.it", "HGTV - Home&Garden", 56)]
    [InlineData("id:raisport.it", "Rai Sport + HD", 58)]
    [InlineData("id:motortrend.it", "Discovery Turbo", 59)]
    [InlineData("id:man-ga", "MAN-GA", 236)]
    [InlineData("Canale.5.it", "", 5)]                 // tvg-id della guida XMLTV
    [InlineData("", "Rai 1 HD", 1)]                    // solo il nome
    [InlineData("name:italia1", "Italia 1", 6)]
    public void NumeroDalTvgIdODalNome(string key, string name, int expected) =>
        Assert.Equal(expected, ChannelNumbers.For(key, name));

    [Theory]
    [InlineData("id:rtl102.5news.it", "RTL 102.5 News")]  // non confondere con RTL 102.5
    [InlineData("id:pluto tv", "Pluto Film TV")]
    [InlineData("id:retemia", "Rete Mia")]
    public void SenzaNumeroNazionale(string key, string name) => Assert.Null(ChannelNumbers.For(key, name));

    [Fact]
    public void CanaliNellOrdineDellaPlaylist_ComeRegistraTV()
    {
        string db = Path.Combine(Path.GetTempPath(), "dlf-ord-" + Guid.NewGuid().ToString("N"), "dlf.db");
        try
        {
            var now = DateTimeOffset.UtcNow;
            var s = new Data.SourceStore(db);
            // la playlist principale mette IL 61 tra Sportitalia e Donna TV: vale il suo ordine, non la tabella dei numeri
            long main = s.AddSource("principale", "https://example.org/a.m3u", now);
            s.Import(main, M3uParser.Parse(Pl(("rai1.it", "Rai 1"), ("canale5.it", "Canale 5"), ("sportitalia.it", "Sportitalia"),
                ("il61.it", "IL 61"), ("donnatv.it", "Donna TV"))), now);
            // la riserva ha un canale in più e Rai 1 in fondo: Rai 1 resta dove lo mette la principale
            long spare = s.AddSource("riserva", "https://example.org/b.m3u", now);
            s.UpdateSource(spare, priority: 1);
            s.Import(spare, M3uParser.Parse(Pl(("tv8.it", "TV8"), ("rai1.it", "Rai 1"))), now);

            var l = s.Channels();
            Assert.Equal(["Rai 1", "Canale 5", "Sportitalia", "IL 61", "Donna TV", "TV8"], l.Select(c => c.Name));
            Assert.Equal([0, 1, 2, 3, 4, 5], l.Select(c => c.Order));
            Assert.Equal([1, 5, 60, null, 62, 8], l.Select(c => c.Number));

            // riordinata la playlist principale, si riordinano i canali
            s.Import(main, M3uParser.Parse(Pl(("canale5.it", "Canale 5"), ("rai1.it", "Rai 1"))), now);
            Assert.Equal(["Canale 5", "Rai 1", "TV8"], s.Channels().Take(3).Select(c => c.Name));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(Path.GetDirectoryName(db)!, true); } catch (IOException) { }
        }
    }

    static string Pl(params (string Id, string Name)[] channels) =>
        "#EXTM3U\n" + string.Concat(channels.Select(c => $"#EXTINF:-1 tvg-id=\"{c.Id}\",{c.Name}\nhttps://example.org/{c.Id}.m3u8\n"));

    [Fact]
    public void OrdineComeSulTelevisore()
    {
        var l = new List<(int? N, string Name)> { (null, "Rete Mia"), (20, "20 Mediaset"), (5, "Canale 5"), (null, "7Gold"), (1, "Rai 1") };
        l.Sort((a, b) => ChannelNumbers.Compare(a.N, a.Name, b.N, b.Name));
        Assert.Equal(["Rai 1", "Canale 5", "20 Mediaset", "7Gold", "Rete Mia"], l.Select(x => x.Name));
    }
}
