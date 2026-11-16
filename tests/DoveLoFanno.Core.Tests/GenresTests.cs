using DoveLoFanno.Epg;

namespace DoveLoFanno.Tests;

/// <summary>Casi presi dai collaudi di RegistraTV (13-14/11/2026).</summary>
public class GenresTests
{
    static EpgProgramme P(int minutes, string episode = "", string title = "Titolo", params string[] cats)
    {
        var start = new DateTime(2026, 11, 16, 21, 0, 0);
        return new EpgProgramme { Title = title, Start = start, Stop = start.AddMinutes(minutes), Episode = episode, Categories = cats };
    }

    static EpgProgramme Classified(EpgProgramme p, string hint = "canale")
    {
        Genres.Classify(p, hint);
        return p;
    }

    [Fact]
    public void FilmConSottogenere()
    {
        var p = Classified(P(110, cats: ["Film", "Commedia"]));
        Assert.Equal(("Film", "Commedia", false), (p.Group, p.Sub, p.GroupGuessed));
        Assert.Equal("Film · Commedia", p.Genre);
    }

    [Fact]
    public void FilmConEpisodioESerieTv_Piedone()
    {
        var p = Classified(P(100, episode: "S1 E2", title: "Piedone", cats: ["Film", "Poliziesco"]));
        Assert.Equal("Serie TV", p.Group);
    }

    [Fact]
    public void FilmSottoVentiMinutiEAltro()
    {
        Assert.Equal("Altro", Classified(P(15, cats: ["Film"])).Group);
    }

    [Theory]
    [InlineData(45, "Serie TV")]   // NCIS, The Mentalist
    [InlineData(69, "Serie TV")]
    [InlineData(70, "Film")]
    public void SottogenereDaFilmSenzaParolaFilm_ContaSoloDa70Minuti(int minutes, string expected)
    {
        Assert.Equal(expected, Classified(P(minutes, title: "NCIS", cats: ["Poliziesco"])).Group);
    }

    [Fact]
    public void SenzaCategorie_TitoloDaTelegiornale()
    {
        var p = Classified(P(30, title: "TG1"), "Rai1.it Rai 1");
        Assert.Equal(("Notizie", true), (p.Group, p.GroupGuessed));
    }

    [Theory]
    [InlineData(100, "", "Film")]
    [InlineData(45, "", "Altro")]
    [InlineData(100, "S2 E3", "Serie TV")]
    public void SenzaCategorie_CanaleDiFilm(int minutes, string episode, string expected)
    {
        Assert.Equal(expected, Classified(P(minutes, episode: episode, title: "Phenomenon"), "Rai Movie it Rai Movie").Group);
    }

    [Fact]
    public void InformazioneESportESport()
    {
        Assert.Equal("Sport", Classified(P(30, cats: ["Informazione", "Sport"])).Group);
    }

    [Fact]
    public void DocumentariMaNonCucina()
    {
        Assert.Equal("Documentari", Classified(P(60, cats: ["Natura"])).Group);
        Assert.Equal("Intrattenimento", Classified(P(60, cats: ["Viaggi", "Cucina"])).Group);
    }

    [Fact]
    public void WorkKey_FilmPerTitoloEAnno()
    {
        var p = Classified(P(110, title: "Il Padrino", cats: ["Film", "Drammatico"]));
        p.Year = 1972;
        Assert.Equal("padrino|1972", p.WorkKey);
    }

    [Fact]
    public void WorkKey_SenzaEpisodioOgniPassaggioEASe()
    {
        var p = Classified(P(30, title: "Striscia la notizia", cats: ["Intrattenimento"]));
        Assert.Equal("striscia la notizia|@202611162100", p.WorkKey);
    }
}
