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
         "original_title":"The Godfather","release_date":"1972-03-14","genres":[{"id":18,"name":"Dramma"},{"id":80,"name":"Crime"}],
         "credits":{"cast":[{"name":"James Caan","order":2},{"name":"Marlon Brando","order":0},{"name":"Al Pacino","order":1}],
                    "crew":[{"name":"Mario Puzo","job":"Screenplay"},{"name":"Francis Ford Coppola","job":"Director"}]},
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
        Assert.Equal(("Francis Ford Coppola", "Marlon Brando, Al Pacino, James Caan"), (d.Directors, d.Actors));
    }

    [Fact]
    public void PersonaDaParteDelNome()
    {
        TmdbPerson[] people = [new(1, "Tim Allen", "Acting", 5), new(2, "Woody Allen", "Directing", 9), new(3, "Allen Covert", "Acting", 2),
            new(4, "Karen Allen", "Costume", 50)];
        Assert.Equal(2, TmdbClient.PersonFor("allen", people)?.Id);           // la più nota tra registi e attori
        Assert.Equal(1, TmdbClient.PersonFor("tim allen", people)?.Id);       // il nome esatto vince
        Assert.Equal(2, TmdbClient.PersonFor("woo", people)?.Id);
        Assert.Null(TmdbClient.PersonFor("llen", people));
        Assert.Null(TmdbClient.PersonFor("allen x", people));
    }

    [Fact]
    public void DettagliTmdbSerie_IdeatoriECastDiTutteLeStagioni()
    {
        using var doc = JsonDocument.Parse("""
            {"name":"Serie","created_by":[{"name":"Ideatrice"}],
             "aggregate_credits":{"cast":[{"name":"B","order":1},{"name":"A","order":0}],"crew":[{"name":"Regista di puntata","jobs":[{"job":"Director"}]}]}}
            """);
        var d = TmdbClient.ParseDetails(doc.RootElement);
        Assert.Equal(("Ideatrice", "A, B"), (d.Directors, d.Actors));
    }

    (WorkStore Works, long Anna, long Padrino, long Ch) Setup()
    {
        var src = new SourceStore(Db);
        long s = src.AddSource("Prova", "https://example.org/x.m3u", Now);
        src.Import(s, M3uParser.Parse("#EXTM3U\n#EXTINF:-1 tvg-id=\"Canale5.it\",Canale 5\nhttp://x/c5.m3u8\n"), Now);
        var guide = new GuideStore(Db);
        guide.Save(guide.AddSource("https://example.org/g.xml"), XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(GuideStoreTests.Xmltv(Now))), Now.LocalDateTime), Now);
        long a = new ProfileStore(Db).Create("Anna", false, null, null, Now).Id;
        var film = guide.Query(new GuideQuery(Now, Now.AddHours(6))).Single(p => p.Title == "Il Padrino");
        return (new WorkStore(Db, guide), a, film.WorkId!.Value, film.ChannelId!.Value);
    }

    [Fact]
    public void SchedaCompleta()
    {
        var (works, a, padrino, ch) = Setup();
        var w = works.Detail(padrino, a, Now)!;
        Assert.Equal(("film", "Il Padrino", 1972), (w.Kind, w.Title, w.Year));
        Assert.Equal(0, w.TmdbYear);
        Assert.Empty(w.Genres);
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
        Assert.Equal(("Francis Ford Coppola", "Marlon Brando, Al Pacino, James Caan"), (w.Directors, w.Actors));
        // anno e generi da TMDB (24/11/2026: la guida di Rai Movie dava 2019 e nessun genere a un western del 1966)
        Assert.Equal(1972, w.TmdbYear);
        Assert.Equal(["Drammatico", "Crime"], w.Genres);

        // la ricerca trova l'opera anche per regista o attore, con una parola intera o il suo inizio
        Assert.Contains(works.Find("coppola", Now), h => h.Id == padrino);
        Assert.Contains(works.Find("francis ford", Now), h => h.Id == padrino);
        Assert.Contains(works.Find("pacin", Now), h => h.Id == padrino);
        Assert.DoesNotContain(works.Find("oppola", Now), h => h.Id == padrino);
        // il regista prima di un titolo che contiene la parola a metà
        long other;
        using (var c = Database.Open(Db))
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO works (kind, work_key, title, title_key, year, updated_at) VALUES ('film', 'scoppola|2000', 'Scoppola', 'scoppola', 2000, 0); SELECT last_insert_rowid();";
            other = (long)cmd.ExecuteScalar()!;
        }
        var found = works.Find("coppola", Now).Select(h => h.Id).ToList();
        Assert.True(found.IndexOf(padrino) < found.IndexOf(other));

        // senza profilo niente stato personale; un'altra opera non c'è
        Assert.Null(works.Detail(padrino, null, Now)!.Stars);
        Assert.Null(works.Detail(999999, a, Now));
    }

    // 24/11/2026: «Per qualche dollaro in più» del 1966 e lo stesso film dato 2019 da Rai Movie erano due opere
    [Fact]
    public void OpereDoppieDelloStessoFilmTmdbUnite()
    {
        var (works, a, padrino, _) = Setup();
        long other;
        using (var c = Database.Open(Db))
        {
            Exec(c, $"UPDATE works SET tmdb_id = 238, tmdb_kind = 'movie' WHERE id = {padrino}");
            Exec(c, "INSERT INTO works (kind, work_key, title, title_key, year, tmdb_id, tmdb_kind, tmdb_year, overview, updated_at) " +
                    "VALUES ('film', 'padrino il|1972', 'Il padrino', 'padrino il', 1972, 238, 'movie', 1972, 'Trama', 0)");
            other = (long)Scalar(c, "SELECT id FROM works WHERE work_key = 'padrino il|1972'");
        }
        // la guida (anno senza conferma di TMDB) ha passaggio, la mia serata e le stelle: tutto va all'opera con l'anno di TMDB
        new TonightStore(Db, new GuideStore(Db)).Add(a, padrino, Now);
        Assert.True(works.SetStars(a, padrino, 4, Now));
        Assert.Equal(1, works.MergeDuplicates(Now));
        Assert.Equal(0, works.MergeDuplicates(Now));

        var w = works.Detail(padrino, a, Now)!;                          // il vecchio collegamento apre l'opera che resta
        Assert.Equal((other, "Trama", 4, true), (w.Id, w.Overview, w.Stars, w.InTonight));
        Assert.Single(w.Airings);
        Assert.DoesNotContain(works.Find("padrino", Now), h => h.Id == padrino);

        // un nuovo caricamento della guida ricrea la stessa chiave: i passaggi vanno all'opera che resta
        var guide = new GuideStore(Db);
        guide.Save(guide.AddSource("https://example.org/g.xml"), XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(GuideStoreTests.Xmltv(Now))), Now.LocalDateTime), Now);
        Assert.Equal(other, guide.Query(new GuideQuery(Now, Now.AddHours(6))).Single(p => p.Title == "Il Padrino").WorkId);

        static void Exec(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        static object Scalar(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar()!; }
    }

    [Fact]
    public void FineSpostataSoloSeInProgrammaOInCorso()
    {
        var (_, _, padrino, ch) = Setup();
        var rec = new RecordingStore(Db);
        long r = rec.Schedule(ch, "Il Padrino", Now.AddMinutes(-10), Now.AddMinutes(50), 2, 10, "mp4", Now, padrino);
        rec.SetState(r, "recording");
        Assert.True(rec.ChangeEnd(r, Now.AddMinutes(80), 5));
        Assert.Equal(Db0(Now.AddMinutes(85)), Db0(rec.Get(r)!.RecordTo));
        Assert.False(rec.ChangeEnd(r, Now.AddMinutes(-20), 0));            // prima dell'inizio
        rec.Finish(r, new RecordingOutcome("completed", "/rec/x.mp4", false, 0, [], [], "x.log"));
        Assert.False(rec.ChangeEnd(r, Now.AddMinutes(90), 5));             // già finita
        static long Db0(DateTimeOffset t) => t.ToUnixTimeSeconds();
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
