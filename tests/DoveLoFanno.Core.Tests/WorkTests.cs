using System.Text;
using System.Text.Json;
using DoveLoFanno.Data;
using DoveLoFanno.Epg;
using DoveLoFanno.Ratings;
using DoveLoFanno.Recording;
using DoveLoFanno.Sources;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

public sealed class WorkTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-work-" + Guid.NewGuid().ToString("N"));
    string Db => Path.Combine(dir, "dlf.db");
    static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    // risposta TMDB ridotta, con la struttura vera di movie/{id}?append_to_response=videos,watch/providers
    const string DetailsJson = """
        {"overview":"La storia della famiglia Corleone.","poster_path":"/poster.jpg","backdrop_path":"/back.jpg","runtime":175,
         "original_title":"The Godfather",
         "videos":{"results":[
            {"site":"YouTube","type":"Teaser","key":"tease","iso_639_1":"it"},
            {"site":"YouTube","type":"Trailer","key":"en1","iso_639_1":"en","official":true},
            {"site":"YouTube","type":"Trailer","key":"it1","iso_639_1":"it","official":false}]},
         "watch/providers":{"results":{
            "IT":{"link":"https://www.themoviedb.org/movie/238-the-godfather/watch?locale=IT",
                  "flatrate":[{"provider_name":"Piattaforma A","logo_path":"/a.jpg"}],
                  "rent":[{"provider_name":"Piattaforma B","logo_path":"/b.jpg"}],
                  "buy":[{"provider_name":"Piattaforma B","logo_path":"/b.jpg"}]},
            "US":{"flatrate":[{"provider_name":"Solo USA"}]}}}}
        """;

    [Fact]
    public void DettagliTmdb_TrailerItalianoEPiattaformeInItalia()
    {
        using var doc = JsonDocument.Parse(DetailsJson);
        var d = TmdbClient.ParseDetails(doc.RootElement);
        Assert.Equal(("La storia della famiglia Corleone.", "/poster.jpg", 175), (d.Overview, d.PosterPath, d.Runtime));
        Assert.Equal("https://www.youtube.com/watch?v=it1", d.Trailer);              // italiano prima del teaser e dell'inglese
        Assert.Equal(3, d.Providers.Count);                                          // niente "Solo USA"
        Assert.Contains(d.Providers, p => p is { Name: "Piattaforma A", Kind: "subscription" });
        Assert.Contains(d.Providers, p => p is { Name: "Piattaforma B", Kind: "rent" });
        Assert.StartsWith("https://www.themoviedb.org/", d.WatchLink);
    }

    (WorkStore Works, long Antonio, long Padrino, long Ch) Setup()
    {
        var src = new SourceStore(Db);
        long s = src.AddSource("Prova", "https://example.org/x.m3u", Now);
        src.Import(s, M3uParser.Parse("#EXTM3U\n#EXTINF:-1 tvg-id=\"Canale5.it\",Canale 5\nhttp://x/c5.m3u8\n"), Now);
        var guide = new GuideStore(Db);
        guide.Save(guide.AddSource("https://example.org/g.xml"), XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(GuideStoreTests.Xmltv(Now))), Now.LocalDateTime), Now);
        long a = new ProfileStore(Db).Create("Antonio", false, null, null, Now).Id;
        var film = guide.Query(new GuideQuery(Now, Now.AddHours(6))).Single(p => p.Title == "Il Padrino");
        return (new WorkStore(Db, guide), a, film.WorkId!.Value, film.ChannelId!.Value);
    }

    [Fact]
    public void SchedaCompleta()
    {
        var (works, a, padrino, ch) = Setup();
        var w = works.Detail(padrino, a, Now)!;
        Assert.Equal(("film", "Il Padrino", 1972), (w.Kind, w.Title, w.Year));
        Assert.Single(w.Airings);
        Assert.Empty(w.InHouse);
        Assert.False(w.InTonight);

        // registrazione riuscita = file in casa; dettagli TMDB salvati con immagini complete
        var rec = new RecordingStore(Db);
        long r = rec.Schedule(ch, "Il Padrino", Now.AddDays(-3), Now.AddDays(-3).AddHours(3), 2, 10, "mp4", Now, padrino);
        rec.Finish(r, new RecordingOutcome("completed", "/rec/Il Padrino (1972).mp4", false, 0, [], [], "x.log"));
        using (var doc = JsonDocument.Parse(DetailsJson)) works.SaveDetails(padrino, TmdbClient.ParseDetails(doc.RootElement), Now);
        new TonightStore(Db, new GuideStore(Db)).Add(a, padrino, Now);
        Assert.True(works.SetStars(a, padrino, 5, Now));

        w = works.Detail(padrino, a, Now)!;
        Assert.Equal("/rec/Il Padrino (1972).mp4", Assert.Single(w.InHouse).File);
        Assert.Equal(WorkStore.ImageBase + "w500/poster.jpg", w.Poster);
        Assert.Equal(("The Godfather", 175, "https://www.youtube.com/watch?v=it1"), (w.OriginalTitle, w.RuntimeMin, w.Trailer));
        Assert.Equal(3, w.Availability.Count);
        Assert.Equal((true, 5), (w.InTonight, w.Stars));
        Assert.NotNull(w.DetailsAt);

        // senza profilo niente stato personale; un'altra opera non c'è
        Assert.Null(works.Detail(padrino, null, Now)!.Stars);
        Assert.Null(works.Detail(999999, a, Now));
    }

    [Fact]
    public void StelleDaUnoACinque()
    {
        var (works, a, padrino, _) = Setup();
        Assert.False(works.SetStars(a, padrino, 6, Now));
        Assert.False(works.SetStars(a, padrino, 0, Now));
        Assert.False(works.SetStars(999, padrino, 3, Now));
        Assert.True(works.SetStars(a, padrino, 3, Now));
        Assert.True(works.SetStars(a, padrino, 4, Now));             // si aggiorna, non si duplica
        Assert.Equal(4, works.Detail(padrino, a, Now)!.Stars);
        Assert.True(works.SetStars(a, padrino, null, Now));
        Assert.Null(works.Detail(padrino, a, Now)!.Stars);
    }

    [Fact]
    public void DettagliDaAggiornareSoloPerFilmConCodiceTmdbEVecchi()
    {
        var (works, _, padrino, _) = Setup();
        Assert.Null(works.DetailsToRefresh(padrino, Now));          // ancora senza codice TMDB
        using (var c = Database.Open(Db))
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "UPDATE works SET tmdb_id = 238 WHERE id = $w";
            cmd.Parameters.AddWithValue("$w", padrino);
            cmd.ExecuteNonQuery();
        }
        Assert.Equal(238L, works.DetailsToRefresh(padrino, Now)?.TmdbId);
        using (var doc = JsonDocument.Parse(DetailsJson)) works.SaveDetails(padrino, TmdbClient.ParseDetails(doc.RootElement), Now);
        Assert.Null(works.DetailsToRefresh(padrino, Now.AddHours(12)));
        Assert.Equal(238L, works.DetailsToRefresh(padrino, Now.AddDays(2))?.TmdbId);
    }
}
