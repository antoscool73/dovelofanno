using System.Text;
using DoveLoFanno.Data;
using DoveLoFanno.Epg;
using DoveLoFanno.Sources;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

public sealed class TonightTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-tonight-" + Guid.NewGuid().ToString("N"));
    string Db => Path.Combine(dir, "dlf.db");
    static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    (TonightStore Tonight, GuideStore Guide, long Anna, long Bimbi, long Padrino) Setup()
    {
        var src = new SourceStore(Db);
        long s = src.AddSource("Prova", "https://example.org/x.m3u", Now);
        src.Import(s, M3uParser.Parse("#EXTM3U\n#EXTINF:-1 tvg-id=\"Canale5.it\",Canale 5\nhttp://x/c5.m3u8\n"), Now);
        var guide = new GuideStore(Db);
        guide.Save(guide.AddSource("https://example.org/g.xml"), XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(GuideStoreTests.Xmltv(Now))), Now.LocalDateTime), Now);
        var profiles = new ProfileStore(Db);
        long a = profiles.Create("Anna", false, null, null, Now).Id;
        long b = profiles.Create("Bambini", true, null, null, Now).Id;
        long padrino = guide.Query(new GuideQuery(Now, Now.AddHours(6))).Single(p => p.Title == "Il Padrino").WorkId!.Value;
        return (new TonightStore(Db, guide), guide, a, b, padrino);
    }

    [Fact]
    public void AggiuntaProssimoPassaggioERimozione()
    {
        var (t, _, a, b, padrino) = Setup();
        Assert.Empty(t.Items(a, Now)!);
        Assert.Equal(TonightResult.Ok, t.Add(a, padrino, Now));
        Assert.Equal(TonightResult.Ok, t.Add(a, padrino, Now));          // due volte: resta una
        var items = t.Items(a, Now)!;
        var it = Assert.Single(items);
        Assert.Equal(("Il Padrino", "film", 1972), (it.Title, it.Kind, it.Year));
        Assert.NotNull(it.Next);
        Assert.Equal("Canale 5", it.Next!.ChannelName);
        Assert.Empty(t.Items(b, Now)!);                                   // la lista è per profilo
        Assert.Equal(TonightResult.NoWork, t.Add(a, 999999, Now));
        Assert.Null(t.Items(424242, Now));
        Assert.Equal(TonightResult.Ok, t.Remove(a, padrino));
        Assert.Empty(t.Items(a, Now)!);
    }

    [Fact]
    public void TitoloSoloInStreaming_ConLeSuePiattaforme()
    {
        var (t, guide, a, _, _) = Setup();
        // un originale trovato con la lente: niente in guida, su Prime Video dalla data di uscita
        long madden = new LibraryStore(Db).FilmWork("Madden", 2026, 1131943, "", "Madden", 0, 0, false, Now);
        new WorkStore(Db, guide).SaveDetails(madden, new DoveLoFanno.Ratings.MovieDetails("", "/m.jpg", "", 110, "Madden", "", "",
            [new("Amazon Prime Video", "subscription", "", "release"), new("Apple TV Store", "rent", "")]), Now);
        t.Add(a, madden, Now);
        var it = Assert.Single(t.Items(a, Now)!);
        Assert.Null(it.Next);
        Assert.Equal(["Amazon Prime Video"], it.Streaming!);          // il noleggio non conta
        Assert.False(it.InHouse);
        Assert.Equal("https://image.tmdb.org/t/p/w342/m.jpg", it.Poster);
        Assert.Equal(["Apple TV Store"], it.Paid!);
    }

    [Fact]
    public void VistoTogleDallaSerataESegnaLOpera()
    {
        var (t, _, a, _, padrino) = Setup();
        t.Add(a, padrino, Now);
        Assert.Equal(TonightResult.Ok, t.Watched(a, padrino, Now));
        Assert.Equal(TonightResult.Ok, t.Watched(a, padrino, Now));      // due volte: nessun doppione
        Assert.Empty(t.Items(a, Now)!);
        using var c = Database.Open(Db);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT count(*), max(watched) FROM watch_states WHERE profile_id = $p AND work_id = $w";
        cmd.Parameters.AddWithValue("$p", a);
        cmd.Parameters.AddWithValue("$w", padrino);
        using var r = cmd.ExecuteReader();
        r.Read();
        Assert.Equal((1L, 1L), (r.GetInt64(0), r.GetInt64(1)));
    }

    [Fact]
    public void VistoSiAnnulla()
    {
        var (t, guide, a, _, padrino) = Setup();
        var works = new WorkStore(Db, guide);
        t.Add(a, padrino, Now);
        Assert.Equal(TonightResult.Ok, t.Watched(a, padrino, Now));
        Assert.True(works.Detail(padrino, a, Now)!.Watched);
        Assert.Empty(t.Items(a, Now)!);                                  // visto: esce dalla serata
        Assert.Equal(TonightResult.Ok, t.Watched(a, padrino, Now, watched: false));
        Assert.False(works.Detail(padrino, a, Now)!.Watched);
        Assert.Equal(TonightResult.NoWork, t.Watched(a, 999999, Now, watched: false));
    }

    // 20/11/2026: Antonio aveva messo nella serata «Geo» e «L'Eredità» del 18/11; ogni puntata è un'opera, quindi finita
    // quella puntata restavano per sempre «Per ora non si vede». Un programma nella serata vale per tutte le puntate.
    [Fact]
    public void ProgrammaVale_PerTutteLePuntate()
    {
        string T(int min) => Now.AddMinutes(min).UtcDateTime.ToString("yyyyMMddHHmmss") + " +0000";
        string xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <tv>
              <channel id="Rai3.it"><display-name>Rai 3</display-name></channel>
              <programme channel="Rai3.it" start="{T(-180)}" stop="{T(-120)}"><title>Geo</title><sub-title>Puntata del 18</sub-title><category>Intrattenimento</category></programme>
              <programme channel="Rai3.it" start="{T(60)}" stop="{T(120)}"><title>Geo</title><sub-title>Puntata del 20</sub-title><category>Intrattenimento</category></programme>
              <programme channel="Rai3.it" start="{T(120)}" stop="{T(240)}"><title>Geo</title><category>Film</category><date>2001</date></programme>
            </tv>
            """;
        var profiles = new ProfileStore(Db);
        long a = profiles.Create("Anna", false, null, null, Now).Id;
        var guide = new GuideStore(Db);
        guide.Save(guide.AddSource("https://example.org/g.xml"), XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), Now.LocalDateTime), Now);
        var t = new TonightStore(Db, guide);
        var all = guide.Query(new GuideQuery(Now.AddHours(-4), Now.AddHours(6))).Where(p => p.Title == "Geo").OrderBy(p => p.Start).ToList();
        Assert.Equal(3, all.Select(p => p.WorkId).Distinct().Count());     // due puntate e un film omonimo: tre opere

        t.Add(a, all[0].WorkId!.Value, Now);                                // la puntata già passata
        var it = Assert.Single(t.Items(a, Now)!);
        Assert.Equal(all[1].Id, it.Next!.Id);                               // la prossima puntata, non il film
        Assert.Equal(all[0].Id, it.Last!.Id);

        t.Remove(a, all[0].WorkId!.Value);
        t.Add(a, all[2].WorkId!.Value, Now);                                // un film resta il suo passaggio
        Assert.Equal(all[2].Id, Assert.Single(t.Items(a, Now)!).Next!.Id);
        Assert.Null(Assert.Single(t.Items(a, Now)!).Last);
    }

    // 20/11/2026: Geo delle 11:04 contava come «da vedere stasera». Evening è il primo passaggio dalle 20 alle 2.
    [Fact]
    public void PassaggioDiSera_DistintoDaQuelloDiGiorno()
    {
        var day = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);          // le 9 in UTC
        string T(int h, int m = 0) => day.Date.AddHours(h).AddMinutes(m).ToString("yyyyMMddHHmmss") + " +0000";
        string xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <tv>
              <channel id="Rai3.it"><display-name>Rai 3</display-name></channel>
              <programme channel="Rai3.it" start="{T(11)}" stop="{T(12)}"><title>Geo</title><sub-title>Mattina</sub-title><category>Intrattenimento</category></programme>
              <programme channel="Rai3.it" start="{T(16)}" stop="{T(17)}"><title>Geo</title><sub-title>Pomeriggio</sub-title><category>Intrattenimento</category></programme>
              <programme channel="Rai3.it" start="{T(21, 30)}" stop="{T(22, 30)}"><title>Geo</title><sub-title>Sera</sub-title><category>Intrattenimento</category></programme>
              <programme channel="Rai3.it" start="{T(13)}" stop="{T(14)}"><title>Solo di giorno</title><category>Intrattenimento</category></programme>
            </tv>
            """;
        long a = new ProfileStore(Db).Create("Anna", false, null, null, day).Id;
        var guide = new GuideStore(Db);
        guide.Save(guide.AddSource("https://example.org/g.xml"), XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), day.LocalDateTime), day);
        var t = new TonightStore(Db, guide);
        var progs = guide.Query(new GuideQuery(day, day.AddHours(15))).ToList();
        t.Add(a, progs.First(p => p.Title == "Geo").WorkId!.Value, day);
        t.Add(a, progs.Single(p => p.Title == "Solo di giorno").WorkId!.Value, day);

        var items = t.Items(a, day, TimeZoneInfo.Utc)!;
        var geo = items.Single(i => i.Title == "Geo");
        Assert.Equal(day.Date.AddHours(11), geo.Next!.Start.UtcDateTime);              // prossimo: quello di giorno
        Assert.Equal(day.Date.AddHours(21.5), geo.Evening!.Start.UtcDateTime);         // di sera: le 21:30
        var only = items.Single(i => i.Title == "Solo di giorno");
        Assert.NotNull(only.Next);
        Assert.Null(only.Evening);
        // a Roma (UTC+1) le 19 UTC sono le 20: anche il passaggio delle 16 UTC resta di giorno, le 21:30 UTC sono sera
        Assert.Equal(day.Date.AddHours(21.5), t.Items(a, day, TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome"))!.Single(i => i.Title == "Geo").Evening!.Start.UtcDateTime);
    }

    [Fact]
    public void SenzaPassaggiFuturiNextVuoto()
    {
        var (t, _, a, _, padrino) = Setup();
        t.Add(a, padrino, Now);
        Assert.Null(t.Items(a, Now.AddDays(2))!.Single().Next);
    }
}
