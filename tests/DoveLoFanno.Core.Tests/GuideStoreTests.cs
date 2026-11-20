using System.Text;
using DoveLoFanno.Data;
using DoveLoFanno.Epg;
using DoveLoFanno.Sources;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

public sealed class GuideStoreTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-guide-" + Guid.NewGuid().ToString("N"));
    string Db => Path.Combine(dir, "dlf.db");

    // guida con orari "adesso + n minuti" (formato XMLTV in UTC)
    internal static string Xmltv(DateTimeOffset now, int shift = 0)
    {
        string T(int min) => now.AddMinutes(min + shift).UtcDateTime.ToString("yyyyMMddHHmmss") + " +0000";
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <tv>
              <channel id="Canale.5.it"><display-name>Canale 5</display-name></channel>
              <channel id="TV8.HD.it"><display-name>TV8 HD</display-name></channel>
              <channel id="TV2000.it"><display-name>TV2000</display-name></channel>
              <programme channel="Canale.5.it" start="{T(60)}" stop="{T(180)}">
                <title lang="it">Il Padrino</title><desc>Una famiglia di New York.</desc>
                <category>Film</category><category>Drammatico</category><date>1972</date>
                <credits><director>Francis Ford Coppola</director><actor>Al Pacino</actor></credits>
                <icon src="https://example.org/padrino.jpg"/>
              </programme>
              <programme channel="Canale.5.it" start="{T(180)}" stop="{T(210)}"><title>Tg5 Notte</title></programme>
              <programme channel="TV8.HD.it" start="{T(0)}" stop="{T(60)}"><title>4 Ristoranti</title><category>Intrattenimento</category></programme>
              <programme channel="TV2000.it" start="{T(30)}" stop="{T(150)}"><title>Città aperta</title><category>Film</category></programme>
            </tv>
            """;
    }

    static readonly DateTimeOffset Now = new(2026, 11, 16, 20, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    (GuideStore Store, long Guide, long C5, long Tv8) Setup()
    {
        var src = new SourceStore(Db);
        long s = src.AddSource("Prova", "https://example.org/x.m3u", Now);
        src.Import(s, M3uParser.Parse("""
            #EXTM3U x-tvg-url="https://example.org/guida.xml.gz"
            #EXTINF:-1 tvg-id="Canale5.it",Canale 5
            http://x/c5.m3u8
            #EXTINF:-1,TV8
            http://x/tv8.m3u8
            """), Now);
        var store = new GuideStore(Db);
        store.SyncFromPlaylists();
        long g = store.Sources().Single().Id;
        var ch = src.Channels();
        return (store, g, ch.Single(c => c.Name == "Canale 5").Id, ch.Single(c => c.Name == "TV8").Id);
    }

    static EpgGuide Load(string xml) => XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), Now.LocalDateTime);

    [Fact]
    public void SalvataggioCollegamentoEFiltri()
    {
        var (store, g, c5, tv8) = Setup();
        Assert.True(store.Sources().Single().FromPlaylist);
        Assert.Equal(4, store.Save(g, Load(Xmltv(Now)), Now));

        var all = store.Query(new GuideQuery(Now, Now.AddHours(6)));
        Assert.Equal(4, all.Count);
        var film = all.Single(p => p.Title == "Il Padrino");
        Assert.Equal((c5, "Canale 5", "Film", "Drammatico", 1972), (film.ChannelId, film.ChannelName, film.Group, film.Sub, film.Year));
        Assert.Equal(("padrino|1972", "https://example.org/padrino.jpg"), (film.WorkKey, film.Icon));
        Assert.Equal(["Film", "Drammatico"], film.Categories);
        Assert.Equal(tv8, all.Single(p => p.Title == "4 Ristoranti").ChannelId);      // "TV8" = "TV8 HD" per nome normalizzato
        Assert.Null(all.Single(p => p.Title == "Città aperta").ChannelId);           // TV2000 non è in playlist: solo avviso

        Assert.Equal(3, store.Query(new GuideQuery(Now, Now.AddHours(6)) { OnlyRecordable = true }).Count);
        Assert.Single(store.Query(new GuideQuery(Now, Now.AddHours(6)) { Text = "pacino" }));
        Assert.Single(store.Query(new GuideQuery(Now, Now.AddHours(6)) { Text = "citta" }));   // senza accenti
        Assert.Equal(2, store.Query(new GuideQuery(Now, Now.AddHours(6)) { Group = "Film" }).Count);
        Assert.Equal(2, store.Query(new GuideQuery(Now, Now.AddHours(6)) { ChannelId = c5 }).Count);
        Assert.Single(store.Query(new GuideQuery(Now.AddMinutes(200), Now.AddHours(6))));      // solo il Tg5 Notte

        var st = store.Status();
        Assert.Equal((3, 2, 4), (st.Channels, st.LinkedChannels, st.Programmes));
        Assert.Equal(store.Programme(film.Id)!.Title, film.Title);
    }

    [Fact]
    public void NuovoCaricamentoSostituisceIProgrammiENonDuplicaLeOpere()
    {
        var (store, g, _, _) = Setup();
        store.Save(g, Load(Xmltv(Now)), Now);
        store.Save(g, Load(Xmltv(Now, shift: 10)), Now.AddHours(12));
        Assert.Equal(4, store.Status().Programmes);
        using var c = Database.Open(Db);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM works WHERE work_key = 'padrino|1972'";
        Assert.Equal(1L, cmd.ExecuteScalar());
    }

    /// <summary>La guida vera (epgshare01, scaricata dalla CI per l'oracolo): salvataggio completo e tempi.</summary>
    [Fact]
    public void GuidaVeraNelDatabase()
    {
        string? file = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "tools", "oracle", "data", "guide.xml.gz");
            if (File.Exists(p)) { file = p; break; }
        }
        if (file == null) return;   // senza la guida scaricata non c'è nulla da controllare
        // l'istantanea della guida è di qualche giorno fa: si lavora a due giorni dalla sua fine, non sull'orologio
        // (con DateTime.Now il test falliva da solo appena l'istantanea finiva, 20/11/2026)
        var at = new DateTimeOffset(XmltvReader.Load(file, DateTime.MinValue.AddDays(2)).LastStop.AddDays(-2));
        var guide = XmltvReader.Load(file, at.LocalDateTime);
        var store = new GuideStore(Db);
        long g = store.AddSource(file);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int saved = store.Save(g, guide, at);
        double saveS = sw.Elapsed.TotalSeconds;
        sw.Restart();
        var evening = store.Query(new GuideQuery(at, at.AddHours(6)));
        double queryS = sw.Elapsed.TotalSeconds;
        Console.WriteLine($"guida vera: {guide.ProgrammeCount} programmi letti, {saved} salvati in {saveS:0.00} s; 6 ore = {evening.Count} programmi in {queryS:0.000} s");
        // (canale, inizio, titolo) doppi si salvano una volta sola
        Assert.InRange(saved, guide.ProgrammeCount * 0.95, guide.ProgrammeCount);
        Assert.True(saveS < 30, $"salvataggio lento: {saveS:0.0} s");
        Assert.True(queryS < 2, $"interrogazione lenta: {queryS:0.00} s");
        Assert.NotEmpty(evening);
    }

    [Fact]
    public void ProgrammaInProgrammaSeUnaRegistrazioneNeCopreMeta()
    {
        var (store, g, c5, _) = Setup();
        store.Save(g, Load(Xmltv(Now)), Now);
        var film = store.Query(new GuideQuery(Now, Now.AddHours(6))).Single(p => p.Title == "Il Padrino");
        Assert.False(film.Scheduled);
        new RecordingStore(Db).Schedule(c5, film.Title, film.Start, film.Stop, 2, 10, "mp4", Now, film.WorkId, null, film.Id);
        var after = store.Query(new GuideQuery(Now, Now.AddHours(6)));
        Assert.True(after.Single(p => p.Title == "Il Padrino").Scheduled);
        Assert.False(after.Single(p => p.Title == "Tg5 Notte").Scheduled);   // coperto solo dal margine di 10 min
        // 20/11/2026: un margine che copre più di metà del programma dopo non lo rende «programmato» (Geo di 16 minuti
        // dopo il Geo in registrazione): contano solo gli orari del programma registrato
        new RecordingStore(Db).Schedule(c5, film.Title, film.Start, film.Stop, 2, 20, "mp4", Now, film.WorkId, null, film.Id);
        Assert.False(store.Query(new GuideQuery(Now, Now.AddHours(6))).Single(p => p.Title == "Tg5 Notte").Scheduled);
    }
}
