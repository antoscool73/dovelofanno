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

    (TonightStore Tonight, GuideStore Guide, long Antonio, long Bimbi, long Padrino) Setup()
    {
        var src = new SourceStore(Db);
        long s = src.AddSource("Prova", "https://example.org/x.m3u", Now);
        src.Import(s, M3uParser.Parse("#EXTM3U\n#EXTINF:-1 tvg-id=\"Canale5.it\",Canale 5\nhttp://x/c5.m3u8\n"), Now);
        var guide = new GuideStore(Db);
        guide.Save(guide.AddSource("https://example.org/g.xml"), XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(GuideStoreTests.Xmltv(Now))), Now.LocalDateTime), Now);
        var profiles = new ProfileStore(Db);
        long a = profiles.Create("Antonio", false, null, null, Now).Id;
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
    public void SenzaPassaggiFuturiNextVuoto()
    {
        var (t, _, a, _, padrino) = Setup();
        t.Add(a, padrino, Now);
        Assert.Null(t.Items(a, Now.AddDays(2))!.Single().Next);
    }
}
