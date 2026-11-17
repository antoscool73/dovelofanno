using System.Globalization;
using System.Text;
using DoveLoFanno.Data;
using DoveLoFanno.Library;
using DoveLoFanno.Ratings;

namespace DoveLoFanno.Server;

public sealed record NewLibraryFolder(string Path, string? Kind);
public sealed record LibraryFolderPatch(string? Kind, bool? Enabled);
public sealed record LibraryStatus(
    bool Scanning, string Status, DateTimeOffset? LastScanAt, ScanReport? LastReport, LibraryCounts Counts, bool HasTmdbKey, string CopyStatus,
    IReadOnlyList<LibraryFolderRow> Folders);
public sealed record ProgressBody(int PositionS, int? DurationS);
public sealed record ProgressReply(bool Watched);
public sealed record MatchBody(string Title, int Year, string Kind, int? TmdbId, int? Season, int? Episode);
public sealed record MatchCandidate(int TmdbId, string Kind, string Title, string OriginalTitle, int Year, int Votes);
public sealed record NewList(string Name, bool Family);
public sealed record ListPatch(string? Name, bool? Family);

/// <summary>API della libreria, del player e delle liste (aggiunte compatibili della wave 6).</summary>
public static class LibraryApi
{
    static readonly string[] Sections = ["all", "films", "series", "documentaries", "programmes", "recordings", "unmatched", "watched"];
    static readonly string[] Sorts = ["added", "title", "rating", "stars"];
    static readonly string[] FolderKinds = ["auto", "films", "series"];
    static readonly string[] WorkKinds = ["film", "series", "documentary", "programme"];

    public static void Map(RouteGroupBuilder api)
    {
        var lib = api.MapGroup("/library").WithTags("Libreria");

        lib.MapGet("", IResult (string? section, long? profileId, string? sort, string? text, ServerContext ctx) =>
        {
            section ??= "all";
            sort ??= "added";
            if (!Sections.Contains(section)) return Results.BadRequest(new ApiError("sezione: " + string.Join(", ", Sections)));
            if (!Sorts.Contains(sort)) return Results.BadRequest(new ApiError("ordine: " + string.Join(", ", Sorts)));
            return Results.Ok(ctx.Library.Entries(section, profileId, sort, text));
        })
            .WithSummary("Opere della libreria per sezione (all, films, series, documentaries, programmes, recordings, unmatched, watched = visti e votati dal profilo) con lo stato del profilo; il profilo bambino vede solo i titoli adatti")
            .Produces<List<LibraryEntry>>().Produces<ApiError>(StatusCodes.Status400BadRequest);

        lib.MapGet("/status", (ServerContext ctx, LibraryHost host) => StatusOf(ctx, host))
            .WithSummary("Stato della libreria: scansione, numeri, cartelle, copie per il browser")
            .Produces<LibraryStatus>();

        lib.MapPost("/scan", (LibraryHost host) => { host.ScanNow(); return Results.Accepted(); })
            .WithSummary("Rilegge subito le cartelle della libreria")
            .Produces(StatusCodes.Status202Accepted);

        // ---------------------------------------------------------------- cartelle
        lib.MapGet("/folders", (ServerContext ctx) => ctx.Library.Folders())
            .WithSummary("Cartelle della libreria").Produces<List<LibraryFolderRow>>();

        lib.MapPost("/folders", IResult (NewLibraryFolder body, ServerContext ctx, LibraryHost host) =>
        {
            string kind = body.Kind ?? "auto";
            if (!FolderKinds.Contains(kind)) return Results.BadRequest(new ApiError("tipo: auto, films o series"));
            string path = (body.Path ?? "").Trim();
            if (path.Length == 0 || !Path.IsPathFullyQualified(path)) return Results.BadRequest(new ApiError("serve il percorso completo della cartella sul server"));
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (!Directory.Exists(path)) return Results.BadRequest(new ApiError("il server non vede questa cartella"));
            if (ctx.Library.AddFolder(path, kind) is not long id) return Results.Conflict(new ApiError("questa cartella c'è già"));
            host.ScanNow();
            return Results.Created($"/api/v1/library/folders/{id}", ctx.Library.Folder(id));
        })
            .WithSummary("Aggiunge una cartella del server (Film, Serie...) e la scansiona")
            .Produces<LibraryFolderRow>(StatusCodes.Status201Created).Produces<ApiError>(StatusCodes.Status400BadRequest).Produces<ApiError>(StatusCodes.Status409Conflict);

        lib.MapPatch("/folders/{id:long}", IResult (long id, LibraryFolderPatch body, ServerContext ctx, LibraryHost host) =>
        {
            if (body.Kind != null && !FolderKinds.Contains(body.Kind)) return Results.BadRequest(new ApiError("tipo: auto, films o series"));
            if (!ctx.Library.UpdateFolder(id, body.Kind, body.Enabled)) return Results.NotFound();
            host.ScanNow();
            return Results.Ok(ctx.Library.Folder(id));
        })
            .WithSummary("Cambia il tipo di una cartella o la sospende").Produces<LibraryFolderRow>().Produces(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status400BadRequest);

        lib.MapDelete("/folders/{id:long}", IResult (long id, ServerContext ctx) =>
            ctx.Library.DeleteFolder(id) ? Results.NoContent() : Results.NotFound())
            .WithSummary("Toglie la cartella dalla libreria (i file sul disco restano)")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------- file e player
        lib.MapGet("/items/{id:long}", IResult (long id, long? profileId, ServerContext ctx) =>
            ctx.Library.Item(id, profileId) is { } it ? Results.Ok(it) : Results.NotFound())
            .WithSummary("Un file per il player: titolo, durata, tracce, sottotitoli, ripresa del profilo, episodio successivo")
            .Produces<LibraryItemDetail>().Produces(StatusCodes.Status404NotFound);

        lib.MapGet("/items/{id:long}/stream", IResult (long id, ServerContext ctx) =>
        {
            if (ctx.Library.StreamPath(id) is not { } s || !File.Exists(s.Path)) return Results.NotFound();
            if (!s.Ready) return Results.Conflict(new ApiError("per guardarlo nel browser serve prima una copia (stato: " + (s.CopyState.Length > 0 ? s.CopyState : "da fare") + ")"));
            return Results.File(s.Path, ContentType(s.Path), enableRangeProcessing: true);
        })
            .WithSummary("Il video per il browser, con richieste a intervalli (per saltare avanti e indietro)")
            .Produces(StatusCodes.Status200OK, contentType: "video/mp4").Produces(StatusCodes.Status206PartialContent)
            .Produces(StatusCodes.Status404NotFound).Produces<ApiError>(StatusCodes.Status409Conflict);

        lib.MapPost("/items/{id:long}/prepare", IResult (long id, ServerContext ctx, LibraryHost host) =>
        {
            if (ctx.Library.StreamPath(id) is not { } s) return Results.NotFound();
            if (s.Ready) return Results.NoContent();
            if (s.CopyState is "" or "done") return Results.NoContent();
            if (!ctx.Library.QueueCopy(id)) return Results.Conflict(new ApiError("il file non si legge: non si può preparare"));
            host.CopyQueued();
            return Results.Accepted();
        })
            .WithSummary("Prepara la copia per il browser (MKV, TS, audio AC3, HEVC...): 202 in coda, 204 se si guarda già così")
            .Produces(StatusCodes.Status202Accepted).Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status409Conflict);

        lib.MapGet("/items/{id:long}/subtitles/{track}", async Task<IResult> (long id, string track, ServerContext ctx, CancellationToken ct) =>
        {
            if (ctx.Library.ItemPath(id) is not { } path) return Results.NotFound();
            if (track.Length < 2 || !int.TryParse(track.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return Results.NotFound();
            if (track[0] == 'f')
            {
                var files = LibraryStore.SidecarSubtitles(path);
                if (n >= files.Count) return Results.NotFound();
                string text = ReadText(files[n]);
                if (Path.GetExtension(files[n]).Equals(".srt", StringComparison.OrdinalIgnoreCase)) text = MediaProbe.SrtToVtt(text);
                return Results.Text(text, "text/vtt", Encoding.UTF8);
            }
            if (track[0] != 's' || ctx.Library.StoredProbe(id) is not { } probe || n >= probe.Subtitles.Count || !MediaProbe.IsTextSubtitle(probe.Subtitles[n].Codec))
                return Results.NotFound();
            string dir = Path.Combine(ctx.DataDir, "subs");
            Directory.CreateDirectory(dir);
            string vtt = Path.Combine(dir, $"{id}-s{n}.vtt");
            if (!File.Exists(vtt) || File.GetLastWriteTimeUtc(vtt) < File.GetLastWriteTimeUtc(path))
            {
                string tmp = vtt + ".tmp";
                int code = await ctx.Tools.RunFfmpegAsync(["-hide_banner", "-y", "-i", path, "-map", $"0:s:{n}", "-f", "webvtt", tmp],
                    Path.Combine(ctx.LogDir, $"sottotitoli-{id}.log"), ct);
                if (code != 0 || !File.Exists(tmp)) return Results.NotFound();
                File.Move(tmp, vtt, true);
            }
            return Results.File(vtt, "text/vtt");
        })
            .WithSummary("Sottotitoli in WebVTT: s0, s1... dentro il file; f0, f1... file .srt o .vtt accanto")
            .Produces(StatusCodes.Status200OK, contentType: "text/vtt").Produces(StatusCodes.Status404NotFound);

        lib.MapPut("/items/{id:long}/match", async Task<IResult> (long id, MatchBody body, ServerContext ctx, CancellationToken ct) =>
        {
            if (ctx.Library.ItemPath(id) == null) return Results.NotFound();
            string title = (body.Title ?? "").Trim();
            if (title.Length == 0 || !WorkKinds.Contains(body.Kind)) return Results.BadRequest(new ApiError("servono titolo e tipo (film, series, documentary, programme)"));
            var now = ctx.Clock.GetUtcNow();
            bool series = body.Kind == "series" || (body.Kind == "documentary" && body.Season != null);
            long top;
            if (body.TmdbId is int tmdb)
                top = series
                    ? ctx.Library.SeriesWork(title, body.Year, tmdb, "", "", 0, 0, body.Kind == "documentary", now)
                    : ctx.Library.FilmWork(title, body.Year, tmdb, "", "", 0, 0, body.Kind == "documentary", now);
            else
                top = ctx.Library.LocalWork(title, body.Year, series ? "series" : body.Kind, now);
            long work = series && body.Season is int s && body.Episode is int e ? ctx.Library.EpisodeWork(top, s, e, "", now) : top;
            ctx.Library.SetMatch(id, work, "manual");
            await RefreshDetailsAsync(ctx, top, ct);
            return Results.Ok(ctx.Library.Item(id, null));
        })
            .WithSummary("Abbinamento a mano di un file: a un risultato TMDB (tmdbId) o solo con titolo e anno")
            .Produces<LibraryItemDetail>().Produces(StatusCodes.Status404NotFound).Produces<ApiError>(StatusCodes.Status400BadRequest);

        lib.MapDelete("/items/{id:long}", IResult (long id, bool? deleteFile, ServerContext ctx) =>
        {
            if (ctx.Library.Remove(id) is not { } r) return Results.NotFound();
            TryDelete(r.Copy);
            if (deleteFile == true)
            {
                TryDelete(r.Path);
                TryDelete(Path.ChangeExtension(r.Path, ".nfo"));
            }
            return Results.NoContent();
        })
            .WithSummary("Toglie un file dalla libreria; con deleteFile=true lo cancella anche dal disco (con il suo .nfo)")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        lib.MapGet("/match", async Task<IResult> (string q, int? year, string? kind, ServerContext ctx, CancellationToken ct) =>
        {
            string key = ctx.Settings.Get("TmdbKey");
            if (key.Length == 0) return Results.Conflict(new ApiError("per cercare su TMDB serve la chiave (Impostazioni › Voti dei film)"));
            if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest(new ApiError("testo da cercare mancante"));
            var tmdb = new TmdbClient(ctx.Http, key);
            try
            {
                bool tv = kind == "tv";
                var res = tv ? await tmdb.SearchTvAsync(q.Trim(), year ?? 0, ct) : await tmdb.SearchMovieAsync(q.Trim(), year ?? 0, ct);
                return Results.Ok(res.OrderByDescending(m => m.VoteCount).Take(20)
                    .Select(m => new MatchCandidate(m.Id, tv ? "tv" : "movie", m.Title, m.OriginalTitle, m.Year, m.VoteCount)).ToList());
            }
            catch (TmdbKeyException ex) { return Results.Conflict(new ApiError(ex.Message)); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                return Results.Json(new ApiError("TMDB non risponde"), statusCode: StatusCodes.Status502BadGateway);
            }
        })
            .WithSummary("Cerca su TMDB per l'abbinamento a mano (kind = movie o tv)")
            .Produces<List<MatchCandidate>>().Produces<ApiError>(StatusCodes.Status400BadRequest).Produces<ApiError>(StatusCodes.Status409Conflict)
            .Produces<ApiError>(StatusCodes.Status502BadGateway);

        // ---------------------------------------------------------------- per profilo: ripresa, visto, liste
        var prof = api.MapGroup("/profiles").WithTags("Profili");

        prof.MapPut("/{id:long}/items/{itemId:long}/progress", IResult (long id, long itemId, ProgressBody body, ServerContext ctx) =>
            ctx.Library.Progress(id, itemId, body.PositionS, body.DurationS ?? 0, ctx.Clock.GetUtcNow()) switch
            {
                ProgressResult.NotFound => Results.NotFound(),
                var r => Results.Ok(new ProgressReply(r == ProgressResult.Watched)),
            })
            .WithSummary("Punto di ripresa del profilo; oltre il 90% il titolo diventa visto ed esce dalla mia serata")
            .Produces<ProgressReply>().Produces(StatusCodes.Status404NotFound);

        prof.MapPost("/{id:long}/items/{itemId:long}/watched", IResult (long id, long itemId, ServerContext ctx) =>
            ctx.Library.MarkWatched(id, itemId, ctx.Clock.GetUtcNow()) ? Results.NoContent() : Results.NotFound())
            .WithSummary("Segna un file (e la sua opera) come visto dal profilo")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        prof.MapGet("/{id:long}/continue", IResult (long id, ServerContext ctx) =>
        {
            if (ctx.Profiles.Get(id) == null) return Results.NotFound();
            var list = ctx.Library.InProgress(id).Select(x => ctx.Library.Item(x.ItemId, id)).Where(x => x != null).Select(x => x!).ToList();
            return Results.Ok(list);
        })
            .WithSummary("Continua a guardare: i file iniziati e non finiti dal profilo")
            .Produces<List<LibraryItemDetail>>().Produces(StatusCodes.Status404NotFound);

        prof.MapGet("/{id:long}/lists", IResult (long id, ServerContext ctx) =>
            ctx.Lists.Lists(id) is { } l ? Results.Ok(l) : Results.NotFound())
            .WithSummary("Liste che il profilo vede: la mia serata, le sue, quelle di famiglia")
            .Produces<List<ListSummary>>().Produces(StatusCodes.Status404NotFound);

        prof.MapPost("/{id:long}/lists", IResult (long id, NewList body, ServerContext ctx) =>
        {
            var (r, listId) = ctx.Lists.Create(id, body.Name, body.Family, ctx.Clock.GetUtcNow());
            return r == ListResult.Ok ? Results.Created($"/api/v1/profiles/{id}/lists/{listId}", ctx.Lists.Lists(id)!.First(x => x.Id == listId)) : ListError(r);
        })
            .WithSummary("Nuova lista del profilo o della famiglia (solo gli adulti)")
            .Produces<ListSummary>(StatusCodes.Status201Created).Produces(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status400BadRequest).Produces<ApiError>(StatusCodes.Status403Forbidden);

        prof.MapGet("/{id:long}/lists/{listId:long}", IResult (long id, long listId, string? sort, ServerContext ctx) =>
            ctx.Lists.Detail(id, listId, sort ?? "added", ctx.Clock.GetUtcNow()) is { } d ? Results.Ok(d) : Results.NotFound())
            .WithSummary("I titoli di una lista con voto, stelle del profilo, file in casa e prossimo passaggio (sort = added, title, rating, stars)")
            .Produces<ListDetail>().Produces(StatusCodes.Status404NotFound);

        prof.MapPatch("/{id:long}/lists/{listId:long}", IResult (long id, long listId, ListPatch body, ServerContext ctx) =>
            ctx.Lists.Update(id, listId, body.Name, body.Family) is var r && r == ListResult.Ok
                ? Results.Ok(ctx.Lists.Lists(id)!.First(x => x.Id == listId)) : ListError(r))
            .WithSummary("Rinomina una lista o la sposta in famiglia")
            .Produces<ListSummary>().Produces(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status400BadRequest).Produces<ApiError>(StatusCodes.Status403Forbidden).Produces<ApiError>(StatusCodes.Status409Conflict);

        prof.MapDelete("/{id:long}/lists/{listId:long}", IResult (long id, long listId, ServerContext ctx) =>
            ctx.Lists.Delete(id, listId) is var r && r == ListResult.Ok ? Results.NoContent() : ListError(r))
            .WithSummary("Cancella una lista (non la mia serata)")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status403Forbidden).Produces<ApiError>(StatusCodes.Status409Conflict);

        prof.MapPost("/{id:long}/lists/{listId:long}/items", IResult (long id, long listId, WorkRef body, ServerContext ctx) =>
            ctx.Lists.Add(id, listId, body.WorkId, ctx.Clock.GetUtcNow()) is var r && r == ListResult.Ok ? Results.NoContent() : ListError(r))
            .WithSummary("Aggiunge un'opera a una lista (se c'è già non cambia nulla)")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status400BadRequest).Produces<ApiError>(StatusCodes.Status403Forbidden);

        prof.MapDelete("/{id:long}/lists/{listId:long}/items/{workId:long}", IResult (long id, long listId, long workId, ServerContext ctx) =>
            ctx.Lists.Remove(id, listId, workId) is var r && r == ListResult.Ok ? Results.NoContent() : ListError(r))
            .WithSummary("Toglie un'opera da una lista")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces<ApiError>(StatusCodes.Status403Forbidden);
    }

    static LibraryStatus StatusOf(ServerContext ctx, LibraryHost host) =>
        new(host.Scanning, host.Scanner.Status, host.LastScanAt, host.LastReport, ctx.Library.Counts(), ctx.Settings.Get("TmdbKey").Length > 0,
            host.CopyStatus, ctx.Library.Folders());

    /// <summary>Locandina e trama subito dopo un abbinamento a mano (se c'è la chiave TMDB).</summary>
    static async Task RefreshDetailsAsync(ServerContext ctx, long workId, CancellationToken ct)
    {
        string key = ctx.Settings.Get("TmdbKey");
        if (key.Length == 0 || ctx.Works.DetailsToRefresh(workId, ctx.Clock.GetUtcNow()) is not { } t) return;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(8));
        var tmdb = new TmdbClient(ctx.Http, key);
        try
        {
            var d = t.Kind == "tv" ? await tmdb.TvDetailsAsync((int)t.TmdbId, cts.Token) : await tmdb.MovieDetailsAsync((int)t.TmdbId, cts.Token);
            ctx.Works.SaveDetails(workId, d, ctx.Clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TmdbKeyException or System.Text.Json.JsonException) { }
    }

    static IResult ListError(ListResult r) => r switch
    {
        ListResult.NotFound => Results.NotFound(),
        ListResult.Forbidden => Results.Json(new ApiError("le liste di famiglia le cambiano solo gli adulti"), statusCode: StatusCodes.Status403Forbidden),
        ListResult.Fixed => Results.Conflict(new ApiError("«La mia serata in TV» non si rinomina e non si cancella")),
        ListResult.NoWork => Results.BadRequest(new ApiError("opera sconosciuta")),
        _ => Results.BadRequest(new ApiError("nome mancante o più lungo di 60 caratteri, o lista già di famiglia")),
    };

    static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".webm" => "video/webm",
        ".mp3" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        ".ogg" or ".opus" => "audio/ogg",
        ".aac" => "audio/aac",
        _ => "video/mp4",
    };

    /// <summary>Sottotitoli in UTF-8 o, se non lo sono, nella codifica dei vecchi .srt italiani (Windows-1252 ≈ Latin-1).</summary>
    static string ReadText(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        try { return new UTF8Encoding(false, true).GetString(b).TrimStart('﻿'); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(b); }
    }

    static void TryDelete(string path)
    {
        if (path.Length == 0) return;
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
