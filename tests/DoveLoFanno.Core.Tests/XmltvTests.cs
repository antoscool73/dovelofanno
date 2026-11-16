using System.Text;
using DoveLoFanno.Epg;

namespace DoveLoFanno.Tests;

public class XmltvTests
{
    const string Sample = """
        <?xml version="1.0" encoding="UTF-8"?>
        <tv>
          <channel id="Canale.5.it"><display-name>Canale 5</display-name></channel>
          <channel id="TV8.HD.it"><display-name>TV8 HD</display-name></channel>
          <programme channel="Canale.5.it" start="20261116210000 +0100" stop="20261116230000 +0100">
            <title lang="en">The Godfather</title>
            <title lang="it">Il Padrino</title>
            <desc>Una famiglia di New York. Una famiglia di New York.</desc>
            <category>Film</category><category>Drammatico</category>
            <date>1972</date>
            <credits><director>Francis Ford Coppola</director><actor>Marlon Brando</actor><actor>Al Pacino</actor></credits>
          </programme>
          <programme channel="Canale.5.it" start="20261116230000 +0100">
            <title>Tg5 Notte</title>
          </programme>
          <programme channel="Canale.5.it" start="20261117000000 +0100" stop="20261117004500 +0100">
            <title>Walker</title>
            <category>Serie TV</category>
            <episode-num system="xmltv_ns">2.9.0/1</episode-num>
          </programme>
          <programme channel="TV8.HD.it" start="20261110100000 +0100" stop="20261110110000 +0100">
            <title>Finito da giorni</title>
          </programme>
          <programme channel="TV8.HD.it" start="20261116200000 +0100" stop="20261116210000 +0100">
            <title>4 Ristoranti</title>
            <category>Intrattenimento</category>
          </programme>
        </tv>
        """;

    static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0);

    static EpgGuide Load() => XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(Sample)), Now);

    static DateTime Local(int day, int hour, int minute = 0) =>
        new DateTimeOffset(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(1)).LocalDateTime;

    [Fact]
    public void LeggeProgrammiEScartaQuelliFinitiDaPiuDiUnGiorno()
    {
        var g = Load();
        Assert.Equal(2, g.ChannelCount);
        Assert.Equal(4, g.ProgrammeCount);
        Assert.Equal(Local(17, 0, 45), g.LastStop);
    }

    [Fact]
    public void FilmCompleto()
    {
        var p = Load().Programmes("Canale.5.it")[0];
        Assert.Equal("Il Padrino", p.Title);                         // vince lang="it"
        Assert.Equal("Una famiglia di New York.", p.Desc);           // descrizione ripetuta tolta
        Assert.Equal(("Film", "Drammatico", 1972), (p.Group, p.Sub, p.Year));
        Assert.Equal("Francis Ford Coppola", p.Directors);
        Assert.Equal("Marlon Brando, Al Pacino", p.Actors);
        Assert.Equal("padrino|1972", p.WorkKey);
        Assert.Equal(Local(16, 21), p.Start);
    }

    [Fact]
    public void FineMancante_FinisceQuandoIniziaIlSuccessivo()
    {
        var p = Load().Programmes("Canale.5.it")[1];
        Assert.Equal(Local(17, 0), p.Stop);
        Assert.Equal("Notizie", p.Group);
    }

    [Fact]
    public void EpisodioXmltvNsContatoDaZero()
    {
        var p = Load().Programmes("Canale.5.it")[2];
        Assert.Equal("S3 E10", p.Episode);
        Assert.Equal("walker|s3 e10", p.WorkKey);
    }

    [Theory]
    [InlineData("Canale.5.it", "Canale 5", "Canale.5.it")]
    [InlineData("Canale5.it", "", "Canale.5.it")]
    [InlineData("Tv8.it", "TV8", "TV8.HD.it")]
    [InlineData("", "Rai 1", null)]
    public void AbbinamentoDeiCanali(string tvgId, string name, string? expected)
    {
        Assert.Equal(expected, Load().Resolve(tvgId, name));
    }

    [Fact]
    public void RicercaSenzaAccentiNeMaiuscoleAncheTraGliAttori()
    {
        var g = Load();
        var all = g.ChannelIds.ToList();
        Assert.Single(g.Search(all, "pacino", null, null, 0, Now, Now.AddDays(2), 0));
        Assert.Single(g.Search(all, "ristoranti", "Intrattenimento", null, 0, Now, Now.AddDays(2), 0));
        Assert.Empty(g.Search(all, null, "Film", null, 7, Now, Now.AddDays(2), 0)); // nessun servizio voti
    }

    [Fact]
    public void FileNonXmltv()
    {
        var ex = Assert.Throws<InvalidDataException>(() => XmltvReader.Load(new MemoryStream("<html/>"u8.ToArray()), Now));
        Assert.Contains("XMLTV", ex.Message);
    }

    [Theory]
    [InlineData("L' Amica", "amica")]
    [InlineData("L'amica", "amica")]
    [InlineData("Il Padrino", "padrino")]
    [InlineData("Città degli angeli", "citta degli angeli")]
    public void TitleKey(string title, string expected) => Assert.Equal(expected, Titles.TitleKey(title));

    [Theory]
    [InlineData("Rai 1 HD  101", "rai1")]
    [InlineData("Rai.1.HD..101.it", "rai1")]
    [InlineData("TV8.HD.it", "tv8")]
    public void ChannelKey(string name, string expected) => Assert.Equal(expected, Titles.ChannelKey(name));
}
