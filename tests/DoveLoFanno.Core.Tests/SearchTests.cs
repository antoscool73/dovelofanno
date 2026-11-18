using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DoveLoFanno.Data;
using DoveLoFanno.Epg;
using DoveLoFanno.Ratings;
using DoveLoFanno.Server;
using DoveLoFanno.Sources;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace DoveLoFanno.Tests;

/// <summary>Ricerca dalla lente: opere note (guida, casa) e titoli di TMDB, anche solo in streaming.</summary>
public sealed class SearchTests(ITestOutputHelper output) : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-search-" + Guid.NewGuid().ToString("N"));
    string Db => Path.Combine(dir, "dlf.db");
    static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    // risposta di search/multi ridotta: un film, una serie, una persona (da scartare), un titolo vuoto
    const string MultiJson = """
        {"results":[
          {"media_type":"movie","id":1219926,"title":"Madden","original_title":"Madden","release_date":"2026-11-14","poster_path":"/m.jpg",
           "overview":"La vita di John Madden.","vote_average":7.1,"vote_count":12,"popularity":80.5,"genre_ids":[18]},
          {"media_type":"tv","id":42,"name":"Madden Story","original_name":"Madden Story","first_air_date":"2019-03-01","poster_path":"",
           "vote_average":6.0,"vote_count":30,"popularity":3,"genre_ids":[99]},
          {"media_type":"person","id":7,"name":"John Madden"},
          {"media_type":"movie","id":9,"title":""}]}
        """;

    [Fact]
    public void RicercaMultiplaTmdb_FilmESerieSenzaPersone()
    {
        using var doc = JsonDocument.Parse(MultiJson);
        var hits = TmdbClient.ParseMulti(doc.RootElement);
        Assert.Equal(2, hits.Count);
        Assert.Equal((1219926, "movie", "Madden", 2026, "/m.jpg", 12), (hits[0].Id, hits[0].Kind, hits[0].Title, hits[0].Year, hits[0].PosterPath, hits[0].VoteCount));
        Assert.Equal((42, "tv", "Madden Story", 2019), (hits[1].Id, hits[1].Kind, hits[1].Title, hits[1].Year));
        Assert.Equal([99], hits[1].GenreIds);
    }

    // dettagli di "Madden" il giorno dell'uscita: JustWatch ancora vuoto, uscita digitale "Prime Video" negli altri paesi
    const string MaddenJson = """
        {"overview":"La vita di John Madden.","runtime":110,"original_title":"Madden",
         "watch/providers":{"results":{}},
         "release_dates":{"results":[
           {"iso_3166_1":"US","release_dates":[{"type":2,"release_date":"2026-11-18T00:00:00.000Z","note":""},
                                              {"type":4,"release_date":"2026-11-18T00:00:00.000Z","note":"Prime Video"}]},
           {"iso_3166_1":"DE","release_dates":[{"type":4,"release_date":"2026-11-18T00:00:00.000Z","note":"Prime Video"}]}]}}
        """;

    [Fact]
    public void OriginaleAppenaUscito_PiattaformaDallaDataDiUscita()
    {
        var day = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        using var doc = JsonDocument.Parse(MaddenJson);
        var p = Assert.Single(TmdbClient.ParseDetails(doc.RootElement, day).Providers);
        Assert.Equal(("Amazon Prime Video", "subscription", "release"), (p.Name, p.Kind, p.Source));

        // il giorno prima non è ancora uscito: niente
        Assert.Empty(TmdbClient.ParseDetails(doc.RootElement, day.AddDays(-1)).Providers);

        // quando JustWatch indica un abbonamento in Italia vale il suo dato, anche se diverso
        using var jw = JsonDocument.Parse(MaddenJson.Replace("\"watch/providers\":{\"results\":{}}",
            "\"watch/providers\":{\"results\":{\"IT\":{\"link\":\"https://x\",\"flatrate\":[{\"provider_name\":\"Now TV\"}]}}}"));
        Assert.Equal(["Now TV|justwatch"], TmdbClient.ParseDetails(jw.RootElement, day).Providers.Select(x => x.Name + "|" + x.Source));

        // serie: la rete d'origine
        using var tv = JsonDocument.Parse("""{"first_air_date":"2019-07-26","networks":[{"id":1024,"name":"Prime Video"}],"watch/providers":{"results":{}}}""");
        Assert.Equal("Amazon Prime Video", Assert.Single(TmdbClient.ParseDetails(tv.RootElement, day).Providers).Name);
        // una rete qualsiasi (HBO, Rai) non dice nulla sull'Italia
        using var hbo = JsonDocument.Parse("""{"first_air_date":"2019-07-26","networks":[{"id":49,"name":"HBO"}]}""");
        Assert.Empty(TmdbClient.ParseDetails(hbo.RootElement, day).Providers);
    }

    [Fact]
    public void OpereNote_PrimaQuelleInTvSenzaAccentiNeEpisodi()
    {
        var src = new SourceStore(Db);
        long s = src.AddSource("Prova", "https://example.org/x.m3u", Now);
        src.Import(s, M3uParser.Parse("#EXTM3U\n#EXTINF:-1 tvg-id=\"Canale5.it\",Canale 5\nhttp://x/c5.m3u8\n"), Now);
        var guide = new GuideStore(Db);
        guide.Save(guide.AddSource("https://example.org/g.xml"), XmltvReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(GuideStoreTests.Xmltv(Now))), Now.LocalDateTime), Now);
        var works = new WorkStore(Db, guide);
        var lib = new LibraryStore(Db);
        // un film aperto da una ricerca precedente (non passa in TV) e una serie con un episodio
        long vecchio = lib.FilmWork("Il padrino - Parte II", 1974, 240, "", "The Godfather Part II", 9, 100, false, Now);
        long serie = lib.SeriesWork("Città segreta", 2020, 77, "", "", 0, 0, false, Now);
        lib.EpisodeWork(serie, 1, 1, "Città vuota", Now);

        var hits = works.Search("padrino", Now);
        Assert.Equal(["Il Padrino", "Il padrino - Parte II"], hits.Select(h => h.Title));
        Assert.NotNull(hits[0].NextAiring);                 // in guida stasera
        Assert.Null(hits[1].NextAiring);
        Assert.Equal((vecchio, 9.0, "TMDB"), (hits[1].Id, hits[1].Rating!.Value, hits[1].RatingSource));
        Assert.Single(works.Search("CITTA", Now), h => h.Kind == "series");   // la serie sì, l'episodio no
        Assert.Contains(works.Search("godfather", Now), h => h.Id == vecchio);    // anche dal titolo originale
        Assert.Empty(works.Search("p", Now));                                      // almeno 2 lettere

        var map = works.ByTmdb([("movie", 240L), ("tv", 77L), ("movie", 1L)]);
        Assert.Equal((vecchio, serie), (map[("movie", 240L)], map[("tv", 77L)]));
        Assert.False(map.ContainsKey(("movie", 1L)));
    }

    [Fact]
    public async Task ApiSenzaChiaveTmdb_CercaNelleOpereNoteECreaLOpera()
    {
        var tools = HlsSimulator.FindTools();
        if (tools == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DoveLoFanno:DataDir", dir);
            b.UseSetting("DoveLoFanno:Ffmpeg", tools.FfmpegPath);
        });
        var http = factory.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/api/v1/search?q=m")).StatusCode);
        var empty = await http.GetFromJsonAsync<SearchResult>("/api/v1/search?q=madden", Json);
        Assert.Empty(empty!.Hits);
        Assert.Contains("chiave TMDB", empty.TmdbNote);

        // dal risultato TMDB della lente all'opera, e la seconda volta la stessa opera
        var body = new FromTmdbBody(1219926, "movie", "Madden", "Madden", 2026, 7.1, 12, [18]);
        var r1 = await (await http.PostAsJsonAsync("/api/v1/works/from-tmdb", body)).Content.ReadFromJsonAsync<WorkRef>(Json);
        var r2 = await (await http.PostAsJsonAsync("/api/v1/works/from-tmdb", body)).Content.ReadFromJsonAsync<WorkRef>(Json);
        Assert.Equal(r1!.WorkId, r2!.WorkId);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/v1/works/from-tmdb", body with { Kind = "person" })).StatusCode);

        var found = await http.GetFromJsonAsync<SearchResult>("/api/v1/search?q=madden", Json);
        var hit = Assert.Single(found!.Hits);
        Assert.Equal((r1.WorkId, "film", 2026, 1219926), (hit.WorkId, hit.Kind, hit.Year, hit.TmdbId));
        Assert.Null(hit.Rating);                            // 12 voti TMDB: troppo pochi per un voto
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/api/v1/works/{r1.WorkId}")).StatusCode);

        // ricerca avanzata sulle opere note (senza chiave TMDB): un western del 1966 e Madden, nella mia serata
        var west = await (await http.PostAsJsonAsync("/api/v1/works/from-tmdb", new FromTmdbBody(429, "movie", "Il buono, il brutto, il cattivo", "", 1966, 8.5, 9000, [37])))
            .Content.ReadFromJsonAsync<WorkRef>(Json);
        var prof = await (await http.PostAsJsonAsync("/api/v1/profiles", new NewProfile("Anna", false, null, null))).Content.ReadFromJsonAsync<ProfileRow>(Json);
        await http.PostAsJsonAsync($"/api/v1/profiles/{prof!.Id}/tonight", new { workId = r1.WorkId });
        await http.PostAsJsonAsync($"/api/v1/profiles/{prof.Id}/tonight", new { workId = west!.WorkId });
        async Task<List<string>> TitlesOf(string query) =>
            (await http.GetFromJsonAsync<SearchResult>("/api/v1/search?" + query, Json))!.Hits.Select(h => h.Title).ToList();

        Assert.Equal(["Il buono, il brutto, il cattivo"], await TitlesOf("genre=37"));                 // senza testo: solo filtri
        Assert.Equal(["Il buono, il brutto, il cattivo"], await TitlesOf("yearFrom=1960&yearTo=1990"));
        Assert.Equal(["Il buono, il brutto, il cattivo"], await TitlesOf("minRating=8"));               // Madden non ha voto
        Assert.Equal(["Madden"], await TitlesOf("genre=18&kind=film"));
        Assert.Empty(await TitlesOf("kind=series"));
        Assert.Empty(await TitlesOf("avail=home"));                                                     // né in casa né altrove
        Assert.Empty(await TitlesOf("q=madden&yearTo=2000"));
        var w = (await http.GetFromJsonAsync<SearchResult>("/api/v1/search?q=buono&genre=37", Json))!.Hits.Single();
        Assert.Equal(["Western"], w.Genres!);
        var opts = await http.GetFromJsonAsync<SearchOptions>("/api/v1/search/options", Json);
        Assert.Contains(opts!.Genres, g => g is { Id: "37", Name: "Western" });
        Assert.Contains(opts.Countries, c => c is { Id: "IT", Name: "Italia" });
    }

    [Fact]
    public void Disponibilita_UnaQualsiasiDelleCaselle()
    {
        string[] subs = ["Netflix"];
        SearchFilter F(params string[] a) => new(Avail: a.ToHashSet());
        Assert.True(SearchApi.AvailOk(F(), false, false, [], [], subs));                               // nessuna casella = ovunque
        Assert.True(SearchApi.AvailOk(F("mine"), false, false, ["Netflix basic with Ads"], [], subs));
        Assert.False(SearchApi.AvailOk(F("mine"), false, false, ["Amazon Prime Video"], [], subs));
        Assert.True(SearchApi.AvailOk(F("paid"), false, false, ["Amazon Prime Video"], [], subs));    // streaming fuori abbonamento
        Assert.True(SearchApi.AvailOk(F("paid"), false, false, [], ["Apple TV Store"], subs));
        Assert.True(SearchApi.AvailOk(F("tv", "home"), false, true, [], [], subs));
        Assert.False(SearchApi.AvailOk(F("tv"), false, true, [], [], subs));
    }

    // filmografia di una persona (person/{id}/combined_credits ridotta): regia prima, poi recitazione, senza doppioni
    [Fact]
    public void PersoneEFilmografia()
    {
        using var multi = JsonDocument.Parse("""
            {"results":[{"media_type":"person","id":4385,"name":"Sergio Leone","known_for_department":"Directing","popularity":5.2},
                        {"media_type":"movie","id":429,"title":"Il buono, il brutto, il cattivo","release_date":"1966-12-23","genre_ids":[37]}]}
            """);
        var p = Assert.Single(TmdbClient.ParsePeople(multi.RootElement));
        Assert.Equal((4385, "Sergio Leone", "Directing"), (p.Id, p.Name, p.KnownFor));
        using var credits = JsonDocument.Parse("""
            {"cast":[{"media_type":"movie","id":391,"title":"Per un pugno di dollari","release_date":"1964-09-12"},
                     {"media_type":"movie","id":429,"title":"Il buono, il brutto, il cattivo","release_date":"1966-12-23"}],
             "crew":[{"media_type":"movie","id":429,"title":"Il buono, il brutto, il cattivo","release_date":"1966-12-23","job":"Director"},
                     {"media_type":"movie","id":335,"title":"C'era una volta il West","release_date":"1968-12-21","job":"Director"},
                     {"media_type":"movie","id":335,"title":"C'era una volta il West","release_date":"1968-12-21","job":"Writer"}]}
            """);
        var c = TmdbClient.ParseCredits(credits.RootElement);
        Assert.Equal(["429 True", "335 True", "391 False"], c.Select(x => $"{x.Hit.Id} {x.Directed}"));
    }
}
