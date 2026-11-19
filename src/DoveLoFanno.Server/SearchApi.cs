using DoveLoFanno.Data;
using DoveLoFanno.Epg;
using DoveLoFanno.Ratings;

namespace DoveLoFanno.Server;

/// <summary>
/// Un risultato della ricerca: un'opera già nota (WorkId) o un titolo di TMDB ancora senza scheda (TmdbId), che diventa
/// un'opera con <c>POST /works/from-tmdb</c>. Kind = film, series, documentary o programme. Genres, Country e Credits
/// (regia e attori) quando si sanno.
/// </summary>
public sealed record SearchHit(long? WorkId, string Kind, string Title, string OriginalTitle, int Year, string Poster, double? Rating,
    string RatingSource, int? TmdbId, string TmdbKind, string Overview, DateTimeOffset? NextAiring, bool InHouse,
    double VoteAverage, int VoteCount, IReadOnlyList<int> GenreIds, IReadOnlyList<string>? Streaming = null, IReadOnlyList<string>? Paid = null,
    IReadOnlyList<string>? Genres = null, string Country = "", string Credits = "")
{
    /// <summary>Il profilo l'ha tolto con «Non proporre più»: compare solo cercandolo per nome (con i soli filtri no).</summary>
    public bool Excluded { get; init; }
}

/// <summary>Risultati: prima le opere già note, poi TMDB. TmdbNote spiega perché TMDB manca (niente chiave, non risponde).</summary>
public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, string TmdbNote)
{
    /// <summary>Probabilmente ci sono altri risultati: il client li chiede con un limit più alto.</summary>
    public bool More { get; init; }
}

public sealed record FromTmdbBody(int TmdbId, string Kind, string Title, string? OriginalTitle, int Year, double? VoteAverage, int? VoteCount,
    IReadOnlyList<int>? GenreIds);

/// <summary>Un genere della ricerca avanzata (codice TMDB dei film) e un paese (ISO).</summary>
public sealed record SearchOption(string Id, string Name);
public sealed record SearchOptions(IReadOnlyList<SearchOption> Genres, IReadOnlyList<SearchOption> Countries);

/// <summary>
/// Ricerca dalla lente della barra in alto, l'unica dell'app (ricerca avanzata del prototipo, 18/11/2026): titolo, regista
/// o attore, e filtri facoltativi (tipo, genere, anni, paese, voto minimo, disponibilità). Prima le opere note (guida, casa,
/// liste, schede), poi TMDB: ricerca di titoli e persone (filmografia del regista o dell'attore) o, senza testo, discover.
/// </summary>
public static class SearchApi
{
    static readonly int[] ChildGenres = [16, 10751, 10762];
    const int PageSize = 40;         // risultati per volta: il client ne chiede di più (limit) arrivato in fondo
    const int MaxLimit = 400;

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/search/options", () => new SearchOptions(
            [.. TmdbGenres.All.OrderBy(g => g.Name, StringComparer.Create(new System.Globalization.CultureInfo("it-IT"), false)).Select(g => new SearchOption(g.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), g.Name))],
            [.. Countries.All.Select(c => new SearchOption(c.Code, c.Name))]))
            .WithTags("Opere").WithSummary("Generi e paesi tra cui scegliere nei filtri della ricerca");

        api.MapGet("/search", async Task<IResult> (string? q, long? profileId, string? kind, int? genre, int? yearFrom, int? yearTo,
            string? country, double? minRating, string? avail, int? limit, string? genres, bool? hideSeen, bool? hideHome, ServerContext ctx, CancellationToken ct) =>
        {
            string text = (q ?? "").Trim();
            if (text.Length > 100) text = text[..100];
            var availSet = (avail ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(a => a is "tv" or "mine" or "paid" or "home").ToHashSet();
            // genre (uno solo, come prima) e genres (più codici separati da virgola: uno qualsiasi)
            var genreList = (genres ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out int g) ? g : 0).Append(genre ?? 0).Where(g => g > 0).Distinct().Take(20).ToList();
            var f = new SearchFilter(kind is "film" or "series" or "documentary" ? kind : "all", genreList, yearFrom ?? 0, yearTo ?? 0,
                (country ?? "").Trim().ToUpperInvariant(), minRating ?? 0, availSet, hideSeen == true, hideHome == true);
            if (text.Length < 2 && !f.Any) return Results.BadRequest(new ApiError("scrivi almeno 2 lettere o scegli un filtro"));
            if (text.Length < 2) text = "";
            // gli esclusi («Non proporre più») si tolgono dalle ricerche con i soli filtri; cercati per nome restano, segnati
            return Results.Ok(await RunAsync(ctx, text, f, profileId, limit ?? PageSize, dropExcluded: text.Length == 0, useTmdb: true, ct));
        })
            .WithTags("Opere")
            .WithSummary("Ricerca avanzata: titolo, regista o attore, e filtri facoltativi (kind film|series|documentary, genre = codice TMDB, " +
                "genres = più codici, uno qualsiasi, yearFrom, yearTo, country ISO, minRating, avail = tv,mine,paid,home: uno qualsiasi, hideSeen, hideHome); " +
                "prima le opere note, poi TMDB. Profilo bambino: solo titoli per bambini. Senza testo niente titoli «Non proporre più»")
            .Produces<SearchResult>().Produces<ApiError>(StatusCodes.Status400BadRequest);

        api.MapPost("/works/from-tmdb", async Task<IResult> (FromTmdbBody b, ServerContext ctx, CancellationToken ct) =>
        {
            string title = (b.Title ?? "").Trim();
            if (b.TmdbId <= 0 || b.Kind is not ("movie" or "tv") || title.Length == 0 || title.Length > 200)
                return Results.BadRequest(new ApiError("servono codice TMDB, tipo (movie o tv) e titolo"));
            var now = ctx.Clock.GetUtcNow();
            bool documentary = b.GenreIds?.Contains(99) == true;
            long id = b.Kind == "tv"
                ? ctx.Library.SeriesWork(title, b.Year, b.TmdbId, "", b.OriginalTitle ?? "", b.VoteAverage ?? 0, b.VoteCount ?? 0, documentary, now, b.GenreIds)
                : ctx.Library.FilmWork(title, b.Year, b.TmdbId, "", b.OriginalTitle ?? "", b.VoteAverage ?? 0, b.VoteCount ?? 0, documentary, now, b.GenreIds);
            await ImdbAsync(ctx, id, b, ct);
            return Results.Ok(new WorkRef(id));
        })
            .WithTags("Opere")
            .WithSummary("Crea (o ritrova) l'opera di un risultato TMDB della ricerca; la Scheda poi scarica i dettagli")
            .Produces<WorkRef>().Produces<ApiError>(StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// La ricerca avanzata, unica per la pagina Cerca e per le liste dinamiche: prima le opere note (guida, casa, liste,
    /// schede), poi TMDB (titoli, filmografia di una persona o, senza testo, discover). <paramref name="dropExcluded"/>
    /// toglie i titoli «Non proporre più» del profilo (altrimenti restano, segnati); <paramref name="useTmdb"/> = false
    /// cerca solo tra le opere note (le notifiche delle liste, che girano da sole). <paramref name="limit"/> da 1 a 400.
    /// </summary>
    public static async Task<SearchResult> RunAsync(ServerContext ctx, string text, SearchFilter f, long? profileId, int limit, bool dropExcluded,
        bool useTmdb, CancellationToken ct)
    {
        // caricamento progressivo: il client chiede 40, poi 80, 120... (fino a 400); i titoli TMDB da controllare
        // uno per uno (piattaforme, paese) crescono con il limite
        int max = Math.Clamp(limit, 1, MaxLimit);
        int lookups = Math.Max(24, max * 3 / 4);

        var prof = profileId is long pid ? ctx.Profiles.Get(pid) : null;
        bool child = prof is { IsChild: true };
        var subs = prof != null ? ctx.Subscriptions.OfProfile(prof.Id)?.Services ?? [] : ctx.Subscriptions.Family();
        var excluded = prof != null ? ctx.Preferences.ExcludedWorkIds(prof.Id) : [];
        var seen = prof != null && f.HideSeen ? ctx.Preferences.WatchedWorkIds(prof.Id) : [];
        var now = ctx.Clock.GetUtcNow();

        // ---------------------------------------------------------------- opere note
        bool LocalOk(WorkSearchHit w) =>
            f.KindOk(w.Kind) && (text.Length > 0 || w.Kind != "programme")
            && TmdbGenres.Matches(f.GenreList, w.GenreIds, w.GuideGenre)
            && f.YearOk(w.Year)
            && Countries.InGuideText(f.Country, w.Country)
            && (f.MinRating <= 0 || (w.Rating ?? 0) >= f.MinRating)
            && AvailOk(f, w.NextAiring != null, w.InHouse, w.Streaming ?? [], w.Paid ?? [], subs)
            && !(f.HideHome && w.InHouse) && !seen.Contains(w.Id)
            && !(dropExcluded && excluded.Contains(w.Id));
        SearchHit Known(WorkSearchHit w) => FromWork(w) with { Excluded = excluded.Contains(w.Id) };

        var hits = new List<SearchHit>();
        var shown = new HashSet<long>();
        foreach (var w in ctx.Works.Find(text, now, child).Where(LocalOk).Take(max))
        {
            hits.Add(Known(w));
            shown.Add(w.Id);
        }
        if (!useTmdb) return new SearchResult(hits, "") { More = hits.Count >= max && max < MaxLimit };

        // ---------------------------------------------------------------- TMDB
        string note = "";
        string key = ctx.Settings.Get("TmdbKey");
        // un titolo solo su TMDB non è in TV né in casa: con quei soli filtri di disponibilità TMDB non serve
        bool tmdbUseful = f.AvailSet.Count == 0 || f.AvailSet.Contains("mine") || f.AvailSet.Contains("paid");
        if (key.Length == 0) note = "Per cercare anche i titoli che non passano in TV serve la chiave TMDB (Impostazioni › Voti dei film).";
        else if (tmdbUseful && hits.Count < max)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(12 + max / 20));
            var tmdb = new TmdbClient(ctx.Http, key);
            try
            {
                var cands = await CandidatesAsync(tmdb, text, f, max, cts.Token);
                cands = [.. cands.Where(c => !child || c.Hit.GenreIds.Any(g => ChildGenres.Contains(g)))
                    .Where(c => f.KindOk(KindOf(c.Hit)) && TmdbGenres.Matches(f.GenreList, c.Hit.GenreIds, "") && f.YearOk(c.Hit.Year))];

                // titoli già noti: valgono i dati di DoveLoFanno (voto IMDb, passaggi, casa, piattaforme)
                var known = ctx.Works.ByTmdb(cands.Select(c => (c.Hit.Kind, (long)c.Hit.Id)));
                var knownOk = ctx.Works.Find("", now, child, max * 4, known.Values.Distinct().ToList()).Where(LocalOk).ToDictionary(w => w.Id);
                var unknown = cands.Where(c => !known.ContainsKey((c.Hit.Kind, c.Hit.Id))
                    && (f.MinRating <= 0 || (c.Hit.VoteCount >= ListRatingSync.MinTmdbVotes && c.Hit.VoteAverage >= f.MinRating))).ToList();

                // titoli solo su TMDB: piattaforme in Italia sempre (per icone, "dove si vede" e il pulsante giusto) e paese
                // se il filtro c'è e la ricerca non l'ha già applicato, per i primi lookups; gli altri senza piattaforme
                bool needCountry = f.Country.Length > 0 && text.Length > 0;
                bool needAvail = f.AvailSet.Count > 0;
                var checkedList = await Throttled(unknown.Take(lookups), 6, async c =>
                {
                    IReadOnlyList<string> countries = needCountry ? await tmdb.CountriesAsync(c.Hit.Id, c.Hit.Kind, cts.Token) : [];
                    IReadOnlyList<Provider> prov = await tmdb.ProvidersAsync(c.Hit.Id, c.Hit.Kind, cts.Token);
                    return (Key: (c.Hit.Kind, c.Hit.Id), Countries: countries, Providers: prov);
                });
                var checkedOnes = checkedList.ToDictionary(x => x.Key);
                var unknownKeys = unknown.Select(c => (c.Hit.Kind, c.Hit.Id)).ToHashSet();

                // nell'ordine di TMDB, noti e non noti mescolati: chiedendo più risultati quelli già mostrati restano in testa.
                // Se i filtri richiedono di controllare uno per uno, ci si ferma all'ultimo titolo controllato
                bool mustCheck = needCountry || needAvail;
                foreach (var c in cands)
                {
                    if (hits.Count >= max) break;
                    if (known.TryGetValue((c.Hit.Kind, c.Hit.Id), out long wid))
                    {
                        if (!shown.Contains(wid) && knownOk.TryGetValue(wid, out var w))
                        {
                            hits.Add(Known(w) with { Credits = c.Credits.Length > 0 ? c.Credits : CreditsOf(w) });
                            shown.Add(wid);
                        }
                        continue;
                    }
                    if (!unknownKeys.Contains((c.Hit.Kind, c.Hit.Id))) continue;
                    if (!checkedOnes.TryGetValue((c.Hit.Kind, c.Hit.Id), out var x))
                    {
                        if (mustCheck) break;
                        hits.Add(FromTmdb(c.Hit, c.Credits, null, null, ""));     // piattaforme null = non lette
                        continue;
                    }
                    if (needCountry && !x.Countries.Contains(f.Country)) continue;
                    var streaming = x.Providers.Where(p => p.Kind is "subscription" or "free").Select(p => p.Name).Distinct().ToList();
                    var paid = x.Providers.Where(p => p.Kind is "rent" or "buy").Select(p => p.Name).Distinct().ToList();
                    if (needAvail && !AvailOk(f, false, false, streaming, paid, subs)) continue;
                    hits.Add(FromTmdb(c.Hit, c.Credits, streaming, paid,
                        f.Country.Length > 0 && x.Countries.Contains(f.Country) ? Countries.Name(f.Country) : ""));
                }
            }
            catch (TmdbKeyException ex) { note = ex.Message; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                note = "TMDB non risponde: ci sono solo i titoli della guida e di casa.";
            }
        }
        return new SearchResult(hits, note) { More = hits.Count >= max && max < MaxLimit };
    }

    /// <summary>
    /// Disponibilità: nessuna casella = ovunque. Con delle caselle, un titolo compare se ogni modo in cui si vede è tra
    /// quelle scelte (19/11/2026, richiesta di Antonio: una categoria tolta dai filtri non si deve vedere). I modi sono le
    /// icone delle locandine: in TV, in casa, nei miei abbonamenti, e "a pagamento" (noleggio, acquisto o streaming fuori
    /// dai miei abbonamenti) solo se non si vede in nessuno degli altri, come la € gialla. Un titolo che non si vede in
    /// nessun modo compare solo con "Ovunque" (nessuna casella).
    /// </summary>
    internal static bool AvailOk(SearchFilter f, bool tv, bool home, IReadOnlyList<string> streaming, IReadOnlyList<string> paid, IReadOnlyList<string> subs)
    {
        var a = f.AvailSet;
        if (a.Count == 0) return true;
        bool Mine(string s) => subs.Any(m => s.Equals(m, StringComparison.OrdinalIgnoreCase) || s.StartsWith(m + " ", StringComparison.OrdinalIgnoreCase));
        bool mine = streaming.Any(Mine);
        var ways = new List<string>();
        if (tv) ways.Add("tv");
        if (home) ways.Add("home");
        if (mine) ways.Add("mine");
        if (ways.Count == 0 && (paid.Count > 0 || streaming.Any(s => !Mine(s)))) ways.Add("paid");
        return ways.Count > 0 && ways.All(a.Contains);
    }

    static string KindOf(TmdbSearchHit h) => h.GenreIds.Contains(99) ? "documentary" : h.Kind == "tv" ? "series" : "film";

    static string CreditsOf(WorkSearchHit w) =>
        string.Join(" · ", new[] { w.Directors.Length > 0 ? "Regia di " + w.Directors : "", w.Actors.Length > 0 ? "Con " + w.Actors : "" }.Where(s => s.Length > 0));

    static SearchHit FromWork(WorkSearchHit w) => new(w.Id, w.Kind, w.Title, "", w.Year, w.Poster, w.Rating, w.RatingSource, (int?)w.TmdbId, w.TmdbKind,
        w.Overview, w.NextAiring, w.InHouse, 0, 0, w.GenreIds, w.Streaming ?? [], w.Paid ?? [],
        w.GenreIds.Count > 0 ? [.. w.GenreIds.Select(TmdbGenres.Name).Where(n => n.Length > 0)] : w.GuideGenre.Length > 0 ? [w.GuideGenre] : [],
        w.Country, CreditsOf(w));

    static SearchHit FromTmdb(TmdbSearchHit h, string credits, IReadOnlyList<string>? streaming, IReadOnlyList<string>? paid, string country)
    {
        bool rated = h.VoteCount >= ListRatingSync.MinTmdbVotes && h.VoteAverage > 0;
        return new SearchHit(null, KindOf(h), h.Title, h.OriginalTitle == h.Title ? "" : h.OriginalTitle, h.Year,
            WorkStore.ImageUrl(h.PosterPath, "w342"), rated ? Math.Round(h.VoteAverage, 1) : null, rated ? "TMDB" : "",
            h.Id, h.Kind, h.Overview, null, false, h.VoteAverage, h.VoteCount, h.GenreIds, streaming, paid,
            [.. h.GenreIds.Select(TmdbGenres.Name).Where(n => n.Length > 0)], country, credits);
    }

    /// <summary>
    /// Candidati da TMDB: con il testo, i titoli e, se il testo è il nome di una persona (regista, attore), la sua
    /// filmografia per prima; senza testo, discover con i filtri (anche serie se il tipo lo consente).
    /// </summary>
    static async Task<List<(TmdbSearchHit Hit, string Credits)>> CandidatesAsync(TmdbClient tmdb, string text, SearchFilter f, int max, CancellationToken ct)
    {
        var res = new List<(TmdbSearchHit, string)>();
        void Add(TmdbSearchHit h, string credits)
        {
            if (!res.Any(x => x.Item1.Id == h.Id && x.Item1.Kind == h.Kind)) res.Add((h, credits));
        }
        if (text.Length >= 2)
        {
            var (titles, people) = await tmdb.SearchAllAsync(text, ct);
            string k = Titles.TitleKey(text);
            var person = people.Where(p => Titles.TitleKey(p.Name) == k).MaxBy(p => p.Popularity);
            if (person != null)
                foreach (var (h, directed) in (await tmdb.PersonCreditsAsync(person.Id, ct)).OrderByDescending(x => x.Directed).ThenByDescending(x => x.Hit.Popularity))
                    Add(h, (directed ? "Regia di " : "Con ") + person.Name);
            foreach (var h in titles) Add(h, "");
            return res;
        }
        string money = f.AvailSet.Contains("mine") && f.AvailSet.Contains("paid") ? "flatrate|free|ads|rent|buy"
            : f.AvailSet.Contains("mine") ? "flatrate|free|ads" : f.AvailSet.Contains("paid") ? "rent|buy|flatrate" : "";
        // pagina per pagina (film e serie alternati): chiedendo di più, i risultati già mostrati restano in testa nello stesso ordine
        bool movies = f.Kind is "all" or "film" or "documentary", series = f.Kind is "all" or "series";
        int pages = Math.Min(20, max / 20 + 1);
        for (int p = 1; p <= pages; p++)
        {
            var page = new List<TmdbSearchHit>();
            if (movies) page.AddRange(await tmdb.DiscoverAsync("movie", f, money, ct, page: p));
            if (series && p <= pages / 2 + 1) page.AddRange(await tmdb.DiscoverAsync("tv", f, money, ct, page: p));
            if (page.Count == 0) break;
            foreach (var h in page.OrderByDescending(h => h.Popularity)) Add(h, "");
        }
        return res;
    }

    /// <summary>Esegue al massimo <paramref name="parallel"/> chiamate alla volta, nell'ordine dei candidati.</summary>
    static async Task<List<T>> Throttled<TIn, T>(IEnumerable<TIn> items, int parallel, Func<TIn, Task<T>> work)
    {
        using var gate = new SemaphoreSlim(parallel);
        var tasks = items.Select(async x =>
        {
            await gate.WaitAsync();
            try { return await work(x); }
            finally { gate.Release(); }
        }).ToList();
        return [.. await Task.WhenAll(tasks)];
    }

    /// <summary>Codice IMDb e, se i voti IMDb sono attivi e il file c'è, il voto IMDb al posto di quello TMDB.</summary>
    static async Task ImdbAsync(ServerContext ctx, long workId, FromTmdbBody b, CancellationToken ct)
    {
        string key = ctx.Settings.Get("TmdbKey");
        if (key.Length == 0) return;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            var tmdb = new TmdbClient(ctx.Http, key);
            string imdb = b.Kind == "tv" ? await tmdb.TvImdbIdAsync(b.TmdbId, cts.Token) : await tmdb.ImdbIdAsync(b.TmdbId, cts.Token);
            if (imdb.Length == 0) return;
            ctx.Works.SetImdbId(workId, imdb);
            string file = Path.Combine(ctx.DataDir, "ratings", "title.ratings.tsv.gz");
            if (ctx.Settings.Get("ImdbRatings") == "1" && File.Exists(file)
                && ImdbRatings.Read(file, new HashSet<string> { imdb }).TryGetValue(imdb, out var r) && r.Votes > 0)
                ctx.Library.SetImdbRating(workId, r.Rating, r.Votes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TmdbKeyException or System.Text.Json.JsonException or IOException) { }
    }
}
