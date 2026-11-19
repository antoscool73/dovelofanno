using System.Text;
using DoveLoFanno.Data;
using DoveLoFanno.Epg;
using DoveLoFanno.Sources;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

/// <summary>
/// Canali doppi nella guida (epgshare01: "Rai4" e "Rai 4  5021", palinsesti non identici): i programmi del doppione si
/// registrano sul canale della playlist collegato all'altro; quelli presenti su entrambi compaiono una volta sola.
/// </summary>
public sealed class GuideAliasTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-alias-" + Guid.NewGuid().ToString("N"));
    string Db => Path.Combine(dir, "dlf.db");
    static readonly DateTimeOffset Now = new(2026, 11, 18, 20, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public void ProgrammiDelDoppioneRegistrabiliESenzaRipetizioni()
    {
        string T(int min) => Now.AddMinutes(min).UtcDateTime.ToString("yyyyMMddHHmmss") + " +0000";
        string xml = $"""
            <tv>
              <channel id="Rai4.it"><display-name>Rai4</display-name></channel>
              <channel id="Rai.4..5021.it"><display-name>Rai 4  5021</display-name></channel>
              <programme channel="Rai4.it" start="{T(0)}" stop="{T(100)}"><title>Film A</title><category>Film</category></programme>
              <programme channel="Rai.4..5021.it" start="{T(2)}" stop="{T(100)}"><title>Film A</title><category>Film</category></programme>
              <programme channel="Rai.4..5021.it" start="{T(120)}" stop="{T(220)}"><title>Breakdown - La trappola</title><category>Film</category></programme>
            </tv>
            """;
        var src = new SourceStore(Db);
        long s = src.AddSource("Prova", "https://example.org/x.m3u", Now);
        src.Import(s, M3uParser.Parse("#EXTM3U x-tvg-url=\"https://example.org/g.xml\"\n#EXTINF:-1 tvg-id=\"Rai4.it\",Rai 4\nhttp://x/rai4.m3u8\n"), Now);
        var guide = new GuideStore(Db);
        guide.SyncFromPlaylists();
        guide.Save(guide.Sources().Single().Id, XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), Now.LocalDateTime), Now);
        long rai4 = src.Channels().Single().Id;

        var all = guide.Query(new GuideQuery(Now, Now.AddHours(6)));
        Assert.Equal(["Film A", "Breakdown - La trappola"], all.Select(p => p.Title));          // Film A una volta sola
        Assert.All(all, p => Assert.Equal((rai4, "Rai 4"), (p.ChannelId!.Value, p.ChannelName)));  // anche quello del doppione
        Assert.Equal(2, guide.Query(new GuideQuery(Now, Now.AddHours(6)) { OnlyRecordable = true }).Count);
        Assert.Equal(2, guide.Query(new GuideQuery(Now, Now.AddHours(6)) { ChannelId = rai4 }).Count);
        var bd = all.Single(p => p.Title.StartsWith("Breakdown", StringComparison.Ordinal));
        Assert.Equal(rai4, guide.Programme(bd.Id)!.ChannelId);
    }

    /// <summary>
    /// Nomi diversi, stesso numero del digitale terrestre (19/11/2026): la playlist porta a "LA7D" con il tvg-id la7d.it,
    /// la guida ha anche "LA7 CINEMA" con gli stessi programmi, che nella scheda comparivano due volte.
    /// </summary>
    [Fact]
    public void StessoNumeroDelDigitaleTerrestreEStessoCanale()
    {
        string T(int min) => Now.AddMinutes(min).UtcDateTime.ToString("yyyyMMddHHmmss") + " +0000";
        string xml = $"""
            <tv>
              <channel id="LA7D.it"><display-name>LA7D</display-name></channel>
              <channel id="LA7.CINEMA.it"><display-name>LA7 CINEMA</display-name></channel>
              <channel id="LA7.HD.it"><display-name>LA7 HD</display-name></channel>
              <programme channel="LA7D.it" start="{T(0)}" stop="{T(125)}"><title>Re per una notte</title><category>Film</category></programme>
              <programme channel="LA7.CINEMA.it" start="{T(0)}" stop="{T(125)}"><title>Re per una notte</title><category>Film</category></programme>
              <programme channel="LA7.HD.it" start="{T(0)}" stop="{T(60)}"><title>Tg La7</title></programme>
            </tv>
            """;
        var src = new SourceStore(Db);
        long s = src.AddSource("Prova", "https://example.org/x.m3u", Now);
        src.Import(s, M3uParser.Parse("#EXTM3U x-tvg-url=\"https://example.org/g.xml\"\n#EXTINF:-1 tvg-id=\"la7d.it\",LA7 Cinema\nhttp://x/la7c.m3u8\n"), Now);
        var guide = new GuideStore(Db);
        guide.SyncFromPlaylists();
        guide.Save(guide.Sources().Single().Id, XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), Now.LocalDateTime), Now);
        long la7c = src.Channels().Single().Id;

        var all = guide.Query(new GuideQuery(Now, Now.AddHours(6)));
        var film = Assert.Single(all, p => p.Title == "Re per una notte");
        Assert.Equal(la7c, film.ChannelId);
        Assert.Null(all.Single(p => p.Title == "Tg La7").ChannelId);   // LA7 (7) non è LA7 Cinema (29)
    }
}
