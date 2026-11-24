using DoveLoFanno.Data;
using DoveLoFanno.Sources;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

/// <summary>Filtro «Canali» della guida (23/11/2026): regione dal nome, tipo dal database iptv-org, scelta a mano.</summary>
public sealed class ChannelInfoTests
{
    [Theory]
    [InlineData("Bergamo Tv", "Lombardia")]
    [InlineData("Antenna 3 Veneto Nord Est", "Veneto")]
    [InlineData("Canale 74 Sicilia", "Sicilia")]
    [InlineData("12 Tv Parma", "Emilia-Romagna")]
    [InlineData("Calabria tv", "Calabria")]
    [InlineData("Forlì-Cesena Tv", "Emilia-Romagna")]
    [InlineData("Telenorba", "Puglia")]
    [InlineData("Alto Adige Tv", "Trentino-Alto Adige")]
    [InlineData("Valle d'Aosta Tv", "Valle d'Aosta")]
    [InlineData("Rai 1", "")]
    [InlineData("Romagna Uno", "Emilia-Romagna")]                 // non «Roma»
    [InlineData("Comedy Central", "")]                            // non «Como»
    [InlineData("Canale 21", "")]
    public void RegioneDalNome(string name, string region) => Assert.Equal(region, ChannelInfo.RegionOf(name));

    [Fact]
    public void TipoDalDatabaseERadioDalGruppo()
    {
        Assert.Equal("radio", ChannelInfo.For("id:radio105tv", "Radio 105 TV", "Radio").Kind);
        Assert.Equal("radio", ChannelInfo.For("name:virginradio", "Virgin Radio", "Italy").Kind);   // dal nome
        Assert.Equal("news", ChannelInfo.For("id:rainews24", "Rai News 24", "Rai").Kind);
        Assert.Equal("shop", ChannelInfo.For("id:qvc", "QVC", "Italy").Kind);
        Assert.Equal("", ChannelInfo.For("name:canalechenonesiste", "Canale Che Non Esiste", "Italy").Kind);
        // codice vecchio di un canale chiuso, nome di quello che l'ha sostituito: vale quello aperto
        Assert.False(ChannelInfo.For("id:la7d", "LA7 Cinema", "Italy").Closed);
        Assert.True(ChannelInfo.For("id:la7d", "LA7d", "Italy").Closed);
        Assert.Equal(20, ChannelInfo.RegionNames.Count);
    }

    [Fact]
    public void SceltaAManoNellaGuida()
    {
        string db = Path.Combine(Path.GetTempPath(), "dlf-ci-" + Guid.NewGuid().ToString("N"), "dlf.db");
        Directory.CreateDirectory(Path.GetDirectoryName(db)!);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var s = new SourceStore(db);
            long a = s.AddSource("Prova", "https://example.org/a.m3u", now);
            s.Import(a, M3uParser.Parse("#EXTM3U\n#EXTINF:-1 tvg-id=\"BergamoTV.it\" group-title=\"Italy\",Bergamo Tv\nhttps://a.example/bg.m3u8\n"), now);
            var ch = Assert.Single(s.Channels());
            Assert.Equal(("Lombardia", (bool?)null), (ch.Region, ch.GuideShow));
            Assert.True(s.SetGuideShow(ch.Id, false));
            Assert.False(s.Channels().Single().GuideShow);
            Assert.True(s.SetGuideShow(ch.Id, null));
            Assert.Null(s.Channels().Single().GuideShow);
            Assert.False(s.SetGuideShow(999999, true));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(Path.GetDirectoryName(db)!, true);
        }
    }
}
