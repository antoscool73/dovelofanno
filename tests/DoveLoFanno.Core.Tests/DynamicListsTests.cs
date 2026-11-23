using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoveLoFanno.Data;
using DoveLoFanno.Ratings;
using DoveLoFanno.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace DoveLoFanno.Tests;

/// <summary>
/// Liste dinamiche (19/11/2026): la ricerca avanzata salvata in una lista, con più generi, senza i titoli «Non proporre
/// più», i già visti e quelli in casa; le liste nella pagina principale; le preferenze di prima convertite in liste.
/// </summary>
public sealed class DynamicListsTests(ITestOutputHelper output) : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-dyn-" + Guid.NewGuid().ToString("N"));
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public void GeneriDellaGuida_ConvertitiInGeneriTmdb()
    {
        Assert.Equal(53, TmdbGenres.FromGuide("Thriller"));
        Assert.Equal(80, TmdbGenres.FromGuide("Crime / Poliziesco"));
        Assert.Equal(9648, TmdbGenres.FromGuide("Giallo"));
        Assert.Equal(878, TmdbGenres.FromGuide("Fantascienza"));
        Assert.Equal(0, TmdbGenres.FromGuide("Reality"));
        Assert.True(TmdbGenres.Matches([37, 53], [53], ""));             // più generi: ne basta uno
        Assert.False(TmdbGenres.Matches([37, 35], [53], ""));
        Assert.True(TmdbGenres.Matches([], [53], ""));                   // nessuno = tutti
    }

    [Fact]
    public void RicercaSalvata_ValidaELetta()
    {
        Assert.NotEqual("", new SavedQuery().Validate());                                  // né testo né filtri
        Assert.NotEqual("", new SavedQuery(Kind: "persona", Genres: [53]).Validate());
        Assert.NotEqual("", new SavedQuery(Avail: ["cinema"]).Validate());
        var q = new SavedQuery(" Verdone ", "film", [35, 18], 1980, 0, "it", 6.5, ["tv", "home"], HideSeen: true);
        Assert.Equal("", q.Validate());
        var back = SavedQuery.FromJson(q.ToJson())!;
        Assert.Equal(("Verdone", "film", 6.5, true), (back.Q, back.Kind, back.MinRating, back.HideSeen));
        Assert.Equal([35, 18], back.Genres!);
        var f = back.ToFilter();
        Assert.Equal(("IT", 2), (f.Country, f.AvailSet.Count));
        Assert.Null(SavedQuery.FromJson(""));
    }

    [Fact]
    public void Preferenze_DiventanoListeDinamicheUnaVolta()
    {
        string db = Path.Combine(dir, "dlf.db");
        var profiles = new ProfileStore(db);
        long anna = profiles.Create("Anna", false, null, null, DateTimeOffset.UtcNow).Id;
        var guide = new GuideStore(db);
        var prefs = new PreferenceStore(db, guide);
        prefs.Add(anna, "Thriller", "", "Film", "Thriller", 7, true);
        prefs.Add(anna, "Sport", "", "Sport", "", 0, false);
        var notes = prefs.ConvertToLists(DateTimeOffset.UtcNow);
        Assert.Contains(notes, n => n.Contains("Sport"));
        Assert.Empty(prefs.ConvertToLists(DateTimeOffset.UtcNow));          // la seconda volta non rifà nulla

        var lists = new ListStore(db, guide).Lists(anna)!.Where(l => l.Query != null).ToList();
        Assert.Equal(["Thriller", "Sport"], lists.Select(l => l.Name));
        var t = lists[0];
        Assert.True(t.OnHome);                                              // attiva = nella pagina principale
        Assert.False(lists[1].OnHome);
        Assert.Equal(("film", 7.0, true, true), (t.Query!.Kind, t.Query.MinRating, t.Query.HideSeen, t.Query.HideHome));
        Assert.Equal([53], t.Query.Genres!);
        Assert.Equal(["tv"], t.Query.Avail!);
    }

    [Fact]
    public async Task ApiListeDinamiche_RicercaSalvataEsclusiEPaginaPrincipale()
    {
        var tools = HlsSimulator.FindTools();
        if (tools == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DoveLoFanno:DataDir", dir);
            b.UseSetting("DoveLoFanno:Ffmpeg", tools.FfmpegPath);
        });
        var http = factory.CreateClient();
        async Task<long> Work(int tmdb, string title, int year, int[] genres) =>
            (await (await http.PostAsJsonAsync("/api/v1/works/from-tmdb", new FromTmdbBody(tmdb, "movie", title, "", year, 8, 5000, genres)))
                .Content.ReadFromJsonAsync<WorkRef>(Json))!.WorkId;
        long western = await Work(429, "Il buono, il brutto, il cattivo", 1966, [37]);
        long thriller = await Work(807, "Seven", 1995, [53, 80]);
        long gump = await Work(13, "Forrest Gump", 1994, [35, 18]);
        var anna = (await (await http.PostAsJsonAsync("/api/v1/profiles", new NewProfile("Anna", false, null, null))).Content.ReadFromJsonAsync<ProfileRow>(Json))!;
        string P = $"/api/v1/profiles/{anna.Id}";
        // senza chiave TMDB la ricerca con i soli filtri trova le opere note: quelle nelle liste
        foreach (long w in new[] { western, thriller, gump }) await http.PostAsJsonAsync(P + "/tonight", new WorkRef(w));
        async Task<List<string>> Titles(string url) => (await http.GetFromJsonAsync<SearchResult>(url, Json))!.Hits.Select(h => h.Title).OrderBy(t => t).ToList();

        // più generi nella ricerca: uno qualsiasi
        Assert.Equal(["Il buono, il brutto, il cattivo", "Seven"], await Titles($"/api/v1/search?genres=37,53&profileId={anna.Id}"));
        Assert.Equal(["Il buono, il brutto, il cattivo"], await Titles($"/api/v1/search?genres=37&genre=10752&profileId={anna.Id}"));

        // lista dinamica dalla ricerca; non si aggiungono titoli a mano
        var q = new SavedQuery(Genres: [37, 53], HideSeen: true);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(P + "/lists", new NewList("Vuota", false, new SavedQuery()))).StatusCode);
        var created = await http.PostAsJsonAsync(P + "/lists", new NewList("Western e thriller", false, q));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var list = (await created.Content.ReadFromJsonAsync<ListSummary>(Json))!;
        Assert.Equal([37, 53], list.Query!.Genres!);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync($"{P}/lists/{list.Id}/items", new WorkRef(western))).StatusCode);
        Assert.Equal(["Il buono, il brutto, il cattivo", "Seven"], await Titles($"{P}/lists/{list.Id}/results"));

        // già visto: sparisce con «Nascondi quelli già visti»; «Non proporre più»: sparisce dalla lista e dalla ricerca con i
        // soli filtri, ma cercato per nome c'è ancora, segnato
        Assert.Empty((await http.GetFromJsonAsync<List<long>>(P + "/watched", Json))!);
        await http.PostAsJsonAsync(P + "/watched", new WorkRef(western));
        Assert.Equal([western], (await http.GetFromJsonAsync<List<long>>(P + "/watched", Json))!);   // per il pulsante «Visto» (23/11/2026)
        Assert.Equal(["Seven"], await Titles($"{P}/lists/{list.Id}/results"));
        await http.PostAsJsonAsync(P + "/ignored", new WorkRef(thriller));
        Assert.Empty(await Titles($"{P}/lists/{list.Id}/results"));
        Assert.DoesNotContain("Seven", await Titles($"/api/v1/search?genres=53&profileId={anna.Id}"));
        var byName = (await http.GetFromJsonAsync<SearchResult>($"/api/v1/search?q=seven&profileId={anna.Id}", Json))!.Hits.Single();
        Assert.True(byName.Excluded);

        // cambiare la ricerca e metterla nella pagina principale
        var patched = await http.PatchAsJsonAsync($"{P}/lists/{list.Id}", new ListPatch(null, null, new SavedQuery(Genres: [35]), true));
        var l2 = (await patched.Content.ReadFromJsonAsync<ListSummary>(Json))!;
        Assert.Equal([35], l2.Query!.Genres!);
        Assert.True(l2.OnHome);
        Assert.Equal(["Forrest Gump"], await Titles($"{P}/lists/{list.Id}/results"));
        var off = await (await http.PatchAsJsonAsync($"{P}/lists/{list.Id}", new ListPatch(null, null, null, false))).Content.ReadFromJsonAsync<ListSummary>(Json);
        Assert.False(off!.OnHome);

        // una lista a mano non diventa dinamica e non ha risultati di ricerca
        var manual = (await (await http.PostAsJsonAsync(P + "/lists", new NewList("Da vedere", false))).Content.ReadFromJsonAsync<ListSummary>(Json))!;
        Assert.Null(manual.Query);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PatchAsJsonAsync($"{P}/lists/{manual.Id}", new ListPatch(null, null, q))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"{P}/lists/{manual.Id}/results")).StatusCode);
    }
}
