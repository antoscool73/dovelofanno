using System.Text;
using DoveLoFanno.Data;
using DoveLoFanno.Epg;
using DoveLoFanno.Sources;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

/// <summary>«Registra la serie» (20/11/2026): ogni puntata una volta, repliche saltate, puntate mancanti, regola fermata.</summary>
public sealed class SeriesTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-series-" + Guid.NewGuid().ToString("N"));
    string Db => Path.Combine(dir, "dlf.db");
    static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    static readonly SeriesDefaults Defaults = new(2, 10, "mp4", 0);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    (SeriesStore Series, GuideStore Guide, RecordingStore Recs) Setup(string programmes)
    {
        Directory.CreateDirectory(dir);
        var src = new SourceStore(Db);
        src.Import(src.AddSource("Prova", "https://example.org/x.m3u", Now),
            M3uParser.Parse("#EXTM3U\n#EXTINF:-1 tvg-id=\"Rai2.it\",Rai 2\nhttp://x/rai2.m3u8\n#EXTINF:-1 tvg-id=\"Italia1.it\",Italia 1\nhttp://x/i1.m3u8\n"), Now);
        string xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <tv>
              <channel id="Rai2.it"><display-name>Rai 2</display-name></channel>
              <channel id="Italia1.it"><display-name>Italia 1</display-name></channel>
              <channel id="Rai4.it"><display-name>Rai 4</display-name></channel>
              {programmes}
            </tv>
            """;
        var guide = new GuideStore(Db);
        guide.Save(guide.AddSource("https://example.org/g.xml"), XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), Now.LocalDateTime), Now);
        var recs = new RecordingStore(Db);
        return (new SeriesStore(Db, guide, recs), guide, recs);
    }

    static string T(int min) => Now.AddMinutes(min).UtcDateTime.ToString("yyyyMMddHHmmss") + " +0000";
    static string Ep(string ch, int at, string ep, string title = "Walker") =>
        $"""<programme channel="{ch}" start="{T(at)}" stop="{T(at + 45)}"><title>{title}</title><category>Serie TV</category><episode-num system="onscreen">{ep}</episode-num></programme>""";

    [Fact]
    public void OgniPuntataUnaVolta_ReplicheSaltate_PuntateMancanti()
    {
        var (series, guide, recs) = Setup(string.Join("\n",
            Ep("Rai2.it", 60, "S1 E1"),
            Ep("Italia1.it", 2000, "S1 E1"),          // replica su un altro canale: saltata
            Ep("Rai2.it", 3000, "S1 E3"),             // manca la 2
            Ep("Rai4.it", 4000, "S1 E4"),             // canale non nelle playlist
            Ep("Rai2.it", 5000, "S1 E1", "Altra serie")));
        var first = guide.Query(new GuideQuery(Now, Now.AddDays(5))).First(p => p.Title == "Walker");
        long rule = series.Create(null, first.WorkId!.Value, Now)!.Value;
        Assert.Equal(rule, series.Create(null, first.WorkId!.Value, Now));     // una regola sola per titolo

        var c = series.Check(rule, Now, Defaults)!;
        Assert.Equal(2, c.NewlyScheduled);
        Assert.Equal(["scheduled", "replica", "scheduled", "not_recordable"], c.Episodes.Select(e => e.Status));
        Assert.Equal(["S1 E2"], c.Missing);
        Assert.DoesNotContain(c.Episodes, e => e.Programme.Title == "Altra serie");
        var scheduled = recs.List().Where(r => r.State == "scheduled").ToList();
        Assert.Equal(2, scheduled.Count);
        Assert.Equal(["Walker - S1 E1", "Walker - S1 E3"], scheduled.Select(r => r.Title).Order());   // il numero della puntata nel titolo

        // il controllo successivo non riprogramma nulla
        var again = series.Check(rule, Now.AddMinutes(1), Defaults)!;
        Assert.Equal(0, again.NewlyScheduled);
        Assert.Equal(["pending", "replica", "pending", "not_recordable"], again.Episodes.Select(e => e.Status));
        Assert.Empty(series.CheckAll(Now.AddMinutes(2), Defaults));
        var r = Assert.Single(series.Rules(Now));
        Assert.Equal(("Walker", 2, 0), (r.Title, r.Scheduled, r.Recorded));

        // fermata: le registrazioni non ancora partite si annullano
        Assert.True(series.Stop(rule));
        Assert.Empty(series.Rules(Now));
        Assert.All(recs.List(), x => Assert.Equal("cancelled", x.State));
        Assert.Null(series.Check(rule, Now, Defaults));
    }

    [Fact]
    public void LimiteDiRegistrazioniContemporanee()
    {
        var (series, guide, recs) = Setup(Ep("Rai2.it", 60, "S2 E1"));
        var p = guide.Query(new GuideQuery(Now, Now.AddDays(5))).Single();
        long italia1 = new SourceStore(Db).Channels().Single(x => x.Name == "Italia 1").Id;
        recs.Schedule(italia1, "Altro", p.Start, p.Stop, 0, 0, "mp4", Now);       // altro canale, stesso orario
        long rule = series.Create(null, p.WorkId!.Value, Now)!.Value;
        var c = series.Check(rule, Now, Defaults with { MaxParallel = 1 })!;
        Assert.Equal("limit", Assert.Single(c.Episodes).Status);
        Assert.Equal(0, c.NewlyScheduled);
    }

    [Fact]
    public void FilmNoProgrammaSiConLaDescrizioneComeChiave()
    {
        var (series, guide, _) = Setup($"""
            <programme channel="Rai2.it" start="{T(60)}" stop="{T(180)}"><title>Il Padrino</title><category>Film</category><date>1972</date></programme>
            <programme channel="Rai2.it" start="{T(300)}" stop="{T(360)}"><title>Geo</title><category>Intrattenimento</category><desc>Viaggio tra i borghi della Sila e le loro tradizioni.</desc></programme>
            <programme channel="Italia1.it" start="{T(900)}" stop="{T(960)}"><title>Geo</title><category>Intrattenimento</category><desc>Viaggio tra i borghi della Sila e le loro tradizioni.</desc></programme>
            """);
        var progs = guide.Query(new GuideQuery(Now, Now.AddDays(5)));
        Assert.Null(series.Create(null, progs.Single(p => p.Title == "Il Padrino").WorkId!.Value, Now));
        long rule = series.Create(null, progs.First(p => p.Title == "Geo").WorkId!.Value, Now)!.Value;
        var c = series.Check(rule, Now, Defaults)!;
        Assert.Equal(["scheduled", "replica"], c.Episodes.Select(e => e.Status));   // stessa descrizione = stessa puntata
    }

    // 20/11/2026: «Serie, in TV» in Cerca non trovava nulla: le serie della guida sono una puntata per opera
    [Fact]
    public void RicercaTrovaLaSerieDellaGuidaUnaVolta()
    {
        var (_, guide, _) = Setup(string.Join("\n", Ep("Rai2.it", 300, "S1 E2"), Ep("Rai2.it", 60, "S1 E1"), Ep("Italia1.it", 900, "S1 E3")));
        var works = new WorkStore(Db, guide);
        var hit = Assert.Single(works.Find("", Now), h => h.Title == "Walker");
        Assert.Equal("series", hit.Kind);
        Assert.Equal(Now.AddMinutes(60), hit.NextAiring);                    // il primo passaggio di tutte le puntate
        Assert.Single(works.Find("walk", Now), h => h.Title == "Walker");
        var a = new TonightStore(Db, guide).Airings([hit.Id], Now, null, TimeZoneInfo.Utc)[hit.Id];
        Assert.Equal(Now.AddMinutes(60), a.Visible!.Start);
    }

    // 23/11/2026: la finestra di Registra dice quante puntate prima di questa sono registrate, recuperabili o perse
    [Fact]
    public void Anteprima_PuntatePrimaDiQuesta()
    {
        var (series, guide, recs) = Setup(string.Join("\n",
            Ep("Rai2.it", 60, "S2 E5"),
            Ep("Rai2.it", 2000, "S2 E6"),
            Ep("Italia1.it", 3000, "S2 E2"),           // replica registrabile: recuperabile
            Ep("Rai4.it", 4000, "S2 E3"),              // canale non nelle playlist: persa
            Ep("Italia1.it", 5000, "S2 E2")));         // stessa puntata: conta una volta
        var progs = guide.Query(new GuideQuery(Now, Now.AddDays(5)));
        var e5 = progs.First(p => p.Episode == "S2 E5");
        long rai2 = new SourceStore(Db).Channels().Single(x => x.Name == "Rai 2").Id;
        long old = recs.Schedule(rai2, "Walker - S2 E1", Now.AddDays(-7), Now.AddDays(-7).AddMinutes(45), 0, 0, "mp4", Now.AddDays(-8), e5.WorkId);
        recs.SetState(old, "completed");

        var pv = series.Preview(e5.Id, Now)!;
        Assert.Equal(("S2 E5", 4, 1, 1, 2), (pv.Episode, pv.Before, pv.Recorded, pv.Recoverable, pv.Lost));   // E1 registrata, E2 in guida, E3-E4 perse
        Assert.Equal((4, 3), (pv.Episodes, pv.Recordable));                                                   // E5, E6, E2, E3 (Rai 4 no)
        Assert.False(pv.RuleActive);
        Assert.DoesNotContain(recs.List(), r => r.State == "scheduled");                                          // l'anteprima non programma
        series.Create(null, e5.WorkId!.Value, Now);
        Assert.True(series.Preview(e5.Id, Now)!.RuleActive);
        Assert.Equal((0, 0, 0, 0), SeriesStore.CountBefore(null, new HashSet<(int, int)>(), new HashSet<(int, int)>()));
    }

    [Fact]
    public void PuntateMancanti_SoloBuchiPiccoli()
    {
        Assert.Equal(["S6 E3", "S6 E5"], SeriesStore.MissingEpisodes(["S6 E2", "S6 E4", "S6 E6", "", "Puntata 3"]));
        Assert.Empty(SeriesStore.MissingEpisodes(["S1 E1", "S1 E140"]));       // repliche sparse: niente elenco
    }
}
