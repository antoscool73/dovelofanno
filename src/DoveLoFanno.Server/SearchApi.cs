using DoveLoFanno.Data;
using DoveLoFanno.Ratings;

namespace DoveLoFanno.Server;

/// <summary>
/// Un risultato della ricerca: un'opera già nota (WorkId) o un titolo di TMDB ancora senza scheda (TmdbId), che diventa
/// un'opera con <c>POST /works/from-tmdb</c>. Kind = film, series, documentary o programme.
/// </summary>
public sealed record SearchHit(long? WorkId, string Kind, string Title, string OriginalTitle, int Year, string Poster, double? Rating,
    string RatingSource, int? TmdbId, string TmdbKind, string Overview, DateTimeOffset? NextAiring, bool InHouse,
    double VoteAverage, int VoteCount, IReadOnlyList<int> GenreIds, IReadOnlyList<string>? Streaming = null, IReadOnlyList<string>? Paid = null);

/// <summary>Risultati: prima le opere già note, poi TMDB. TmdbNote spiega perché TMDB manca (niente chiave, non risponde).</summary>
public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, string TmdbNote);

public sealed record FromTmdbBody(int TmdbId, string Kind, string Title, string? OriginalTitle, int Year, double? VoteAverage, int? VoteCount,
    IReadOnlyList<int>? GenreIds);

/// <summary>
/// Ricerca dalla lente della barra in alto: titoli in guida e in casa, e qualunque film o serie su TMDB (anche solo in
/// streaming, es. un originale Prime Video), che si apre nella Scheda con «Dove lo vedo» e gli avvisi. Aggiunta del 18/11/2026.
/// </summary>
public static class SearchApi
{
    static readonly int[] ChildGenres = [16, 10751, 10762];

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/search", async Task<IResult> (string q, long? profileId, ServerContext ctx, CancellationToken ct) =>
        {
            string text = (q ?? "").Trim();
            if (text.Length < 2) return Results.BadRequest(new ApiError("scrivi almeno 2 lettere"));
            if (text.Length > 100) text = text[..100];
            bool child = profileId is long pid && ctx.Profiles.Get(pid) is { IsChild: true };
            var now = ctx.Clock.GetUtcNow();

            var hits = ctx.Works.Search(text, now, child).Select(w => new SearchHit(w.Id, w.Kind, w.Title, "", w.Year, w.Poster, w.Rating,
                w.RatingSource, (int?)w.TmdbId, w.TmdbKind, "", w.NextAiring, w.InHouse, 0, 0, [], w.Streaming ?? [], w.Paid ?? [])).ToList();

            string note = "";
            string key = ctx.Settings.Get("TmdbKey");
            if (key.Length == 0) note = "Per cercare anche i titoli che non passano in TV serve la chiave TMDB (Impostazioni › Voti dei film).";
            else
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));
                try
                {
                    var found = (await new TmdbClient(ctx.Http, key).SearchMultiAsync(text, cts.Token))
                        .Where(h => !child || h.GenreIds.Any(g => ChildGenres.Contains(g)))
                        .Take(20).ToList();
                    var known = ctx.Works.ByTmdb(found.Select(h => (h.Kind, (long)h.Id)));
                    var shown = hits.Where(h => h.WorkId != null).Select(h => h.WorkId!.Value).ToHashSet();
                    foreach (var h in found)
                    {
                        long? work = known.TryGetValue((h.Kind, h.Id), out long w) ? w : null;
                        if (work is long id && shown.Contains(id)) continue;        // già tra le opere note, con lo stato in TV o in casa
                        string kind = h.GenreIds.Contains(99) ? "documentary" : h.Kind == "tv" ? "series" : "film";
                        bool rated = h.VoteCount >= 20 && h.VoteAverage > 0;
                        hits.Add(new SearchHit(work, kind, h.Title, h.OriginalTitle == h.Title ? "" : h.OriginalTitle, h.Year,
                            WorkStore.ImageUrl(h.PosterPath, "w342"), rated ? Math.Round(h.VoteAverage, 1) : null, rated ? "TMDB" : "",
                            h.Id, h.Kind, h.Overview, null, false, h.VoteAverage, h.VoteCount, h.GenreIds));
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
            .WithSummary("Cerca per titolo: opere della guida e di casa, poi film e serie di TMDB (anche solo in streaming); profilo bambino: solo titoli per bambini")
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
