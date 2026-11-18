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
    IReadOnlyList<string>? Genres = null, string Country = "", string Credits = "");

/// <summary>Risultati: prima le opere già note, poi TMDB. TmdbNote spiega perché TMDB manca (niente chiave, non risponde).</summary>
public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, string TmdbNote);

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
    const int MaxHits = 40;
    const int MaxLookups = 24;        // titoli TMDB da controllare uno per uno (paese, piattaforme)

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/search/options", () => new SearchOptions(
            [.. TmdbGenres.All.OrderBy(g => g.Name, StringComparer.Create(new System.Globalization.CultureInfo("it-IT"), false)).Select(g => new SearchOption(g.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), g.Name))],
            [.. Countries.All.Select(c => new SearchOption(c.Code, c.Name))]))
            .WithTags("Opere").WithSummary("Generi e paesi tra cui scegliere nei filtri della ricerca");

        api.MapGet("/search", async Task<IResult> (string? q, long? profileId, string? kind, int? genre, int? yearFrom, int? yearTo, string? country,
            double? minRating, string? avail, ServerContext ctx, CancellationToken ct) =>
        {
            string text = (q ?? "").Trim();
            if (text.Length > 100) text = text[..100];
            var availSet = (avail ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(a => a is "tv" or "mine" or "paid" or "home").ToHashSet();
            var f = new SearchFilter(kind is "film" or "series" or "documentary" ? kind : "all", genre ?? 0, yearFrom ?? 0, yearTo ?? 0,
                (country ?? "").Trim().ToUpperInvariant(), minRating ?? 0, availSet);
            if (text.Length < 2 && !f.Any) return Results.BadRequest(new ApiError("scrivi almeno 2 lettere o scegli un filtro"));
            if (text.Length < 2) text = "";

            var prof = profileId is long pid ? ctx.Profiles.Get(pid) : null;
            bool child = prof is { IsChild: true };
            var subs = prof != null ? ctx.Subscriptions.OfProfile(prof.Id)?.Services ?? [] : ctx.Subscriptions.Family();
            var now = ctx.Clock.GetUtcNow();

            // ---------------------------------------------------------------- opere note
            bool LocalOk(WorkSearchHit w) =>
                f.KindOk(w.Kind) && (text.Length > 0 || w.Kind != "programme")
                && TmdbGenres.Matches(f.Genre, w.GenreIds, w.GuideGenre)
                && f.YearOk(w.Year)
                && Countries.InGuideText(f.Country, w.Country)
                && (f.MinRating <= 0 || (w.Rating ?? 0) >= f.MinRating)
                && AvailOk(f, w.NextAiring != null, w.InHouse, w.Streaming ?? [], w.Paid ?? [], subs);

            var hits = new List<SearchHit>();
            var shown = new HashSet<long>();
            foreach (var w in ctx.Works.Find(text, now, child).Where(LocalOk).Take(MaxHits))
            {
                hits.Add(FromWork(w));
                shown.Add(w.Id);
            }

            // ---------------------------------------------------------------- TMDB
            string note = "";
            string key = ctx.Settings.Get("TmdbKey");
            // un titolo solo su TMDB non è in TV né in casa: con quei soli filtri di disponibilità TMDB non serve
            bool tmdbUseful = f.AvailSet.Count == 0 || f.AvailSet.Contains("mine") || f.AvailSet.Contains("paid");
            if (key.Length == 0) note = "Per cercare anche i titoli che non passano in TV serve la chiave TMDB (Impostazioni › Voti dei film).";
            else if (tmdbUseful && hits.Count < MaxHits)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(12));
                var tmdb = new TmdbClient(ctx.Http, key);
                try
                {
                    var cands = await CandidatesAsync(tmdb, text, f, cts.Token);
                    cands = [.. cands.Where(c => !child || c.Hit.GenreIds.Any(g => ChildGenres.Contains(g)))
                        .Where(c => f.KindOk(KindOf(c.Hit)) && TmdbGenres.Matches(f.Genre, c.Hit.GenreIds, "") && f.YearOk(c.Hit.Year))];

                    // titoli già noti: valgono i dati di DoveLoFanno (voto IMDb, passaggi, casa, piattaforme)
                    var known = ctx.Works.ByTmdb(cands.Select(c => (c.Hit.Kind, (long)c.Hit.Id)));
                    var knownOk = ctx.Works.Find("", now, child, MaxHits * 4, known.Values.Distinct().ToList()).Where(LocalOk).ToDictionary(w => w.Id);
                    var unknown = new List<(TmdbSearchHit Hit, string Credits)>();
                    foreach (var c in cands)
                    {
                        if (known.TryGetValue((c.Hit.Kind, c.Hit.Id), out long wid))
                        {
                            if (!shown.Contains(wid) && knownOk.TryGetValue(wid, out var w)) { hits.Add(FromWork(w) with { Credits = c.Credits.Length > 0 ? c.Credits : CreditsOf(w) }); shown.Add(wid); }
                        }
                        else if (f.MinRating <= 0 || (c.Hit.VoteCount >= ListRatingSync.MinTmdbVotes && c.Hit.VoteAverage >= f.MinRating))
                            unknown.Add(c);
                    }

                    // titoli solo su TMDB: paese (se il filtro c'è e la ricerca non l'ha già applicato) e piattaforme (se servono)
                    bool needCountry = f.Country.Length > 0 && text.Length > 0;
                    bool needProviders = f.AvailSet.Count > 0;
                    var checkedOnes = await Throttled(unknown.Take(needCountry || needProviders ? MaxLookups : MaxHits), 4, async c =>
                    {
                        IReadOnlyList<string> countries = [];
                        IReadOnlyList<Provider> prov = [];
                        if (needCountry) countries = await tmdb.CountriesAsync(c.Hit.Id, c.Hit.Kind, cts.Token);
                        if (needProviders) prov = await tmdb.ProvidersAsync(c.Hit.Id, c.Hit.Kind, cts.Token);
                        return (c.Hit, c.Credits, Countries: countries, Providers: prov);
                    });
                    foreach (var x in checkedOnes)
                    {
                        if (hits.Count >= MaxHits) break;
                        if (needCountry && !x.Countries.Contains(f.Country)) continue;
                        var streaming = x.Providers.Where(p => p.Kind is "subscription" or "free").Select(p => p.Name).Distinct().ToList();
                        var paid = x.Providers.Where(p => p.Kind is "rent" or "buy").Select(p => p.Name).Distinct().ToList();
                        if (needProviders && !AvailOk(f, false, false, streaming, paid, subs)) continue;
                        // piattaforme non controllate (nessun filtro di disponibilità): null, così il client non dice "non si vede"
                        hits.Add(FromTmdb(x.Hit, x.Credits, needProviders ? streaming : null, needProviders ? paid : null,
                            f.Country.Length > 0 && x.Countries.Contains(f.Country) ? Countries.Name(f.Country) : ""));
                    }
                }
                catch (TmdbKeyException ex) { note = ex.Message; }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                {
                    note = "TMDB non risponde: ci sono solo i titoli della guida e di casa.";
                }
            }
            return Results.Ok(new SearchResult(hits, note));
        })
            .WithTags("Opere")
            .WithSummary("Ricerca avanzata: titolo, regista o attore, e filtri facoltativi (kind film|series|documentary, genre = codice TMDB, " +
                "yearFrom, yearTo, country ISO, minRating, avail = tv,mine,paid,home: uno qualsiasi); prima le opere note, poi TMDB. Profilo bambino: solo titoli per bambini")
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
    /// Disponibilità: nessuna casella = ovunque; altrimenti ne basta una (in TV, nei miei abbonamenti, a pagamento, in casa).
    /// "A pagamento" = noleggio, acquisto o streaming fuori dai miei abbonamenti.
    /// </summary>
    internal static bool AvailOk(SearchFilter f, bool tv, bool home, IReadOnlyList<string> streaming, IReadOnlyList<string> paid, IReadOnlyList<string> subs)
    {
        var a = f.AvailSet;
        if (a.Count == 0) return true;
        bool Mine(string s) => subs.Any(m => s.Equals(m, StringComparison.OrdinalIgnoreCase) || s.StartsWith(m + " ", StringComparison.OrdinalIgnoreCase));
        return (a.Contains("tv") && tv) || (a.Contains("home") && home)
            || (a.Contains("mine") && streaming.Any(Mine))
            || (a.Contains("paid") && (paid.Count > 0 || streaming.Any(s => !Mine(s))));
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
    static async Task<List<(TmdbSearchHit Hit, string Credits)>> CandidatesAsync(TmdbClient tmdb, string text, SearchFilter f, CancellationToken ct)
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
        var lists = new List<IReadOnlyList<TmdbSearchHit>>();
        if (f.Kind is "all" or "film" or "documentary")
        {
            lists.Add(await tmdb.DiscoverAsync("movie", f, money, ct));
            lists.Add(await tmdb.DiscoverAsync("movie", f, money, ct, page: 2));
        }
        if (f.Kind is "all" or "series") lists.Add(await tmdb.DiscoverAsync("tv", f, money, ct));
        foreach (var h in lists.SelectMany(l => l).OrderByDescending(h => h.Popularity)) Add(h, "");
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
