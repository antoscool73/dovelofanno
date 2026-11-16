using DoveLoFanno.Data;
using DoveLoFanno.Sources;

namespace DoveLoFanno.Server;

public sealed record ApiError(string Error);
public sealed record Health(string Status, string Api, int Database, int Running);
public sealed record NewSource(string Name, string Url);
public sealed record SourcePatch(string? Name, int? MaxConnections, bool? ClearMaxConnections, bool? Enabled, int? Priority);
public sealed record ImportResult(long SourceId, int Channels, int Added, int Removed, int Drm, string EpgUrl);
public sealed record NewRecording(long ChannelId, string Title, DateTimeOffset Start, DateTimeOffset Stop, int? BeforeMin, int? AfterMin, string? Format);
public sealed record ScheduledRecording(long Id, IReadOnlyList<string> Warnings);
public sealed record RecordingDetail(RecordingRow Recording, IReadOnlyList<PartRow> Parts);
public sealed record NewProfile(string Name, bool IsChild, string? Avatar, string? Pin);
public sealed record ProfilePatch(string? Name, bool? IsChild, string? Avatar, string? Pin, bool? ClearPin);
public sealed record PinCheck(string? Pin);

/// <summary>
/// API v1. Congelata con la wave 3 (16/11/2026): il documento docs/api/openapi-v1.json è quello approvato e un test
/// controlla che il server lo produca uguale. Da qui in poi solo aggiunte compatibili (endpoint o campi nuovi, con
/// il documento aggiornato nello stesso commit); un cambiamento incompatibile è la v2.
/// </summary>
public static class Api
{
    public const string Version = "v1";

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapGet("/health", (ServerContext ctx) => new Health("ok", Version, Database.LatestVersion, ctx.Scheduler.Running.Count))
            .WithTags("Sistema").WithSummary("Stato del server");

        // ---------------------------------------------------------------- sorgenti
        var src = api.MapGroup("/sources").WithTags("Sorgenti");

        src.MapGet("", (ServerContext ctx) => ctx.Sources.Sources())
            .WithSummary("Playlist M3U dell'utente");

        src.MapPost("", async Task<IResult> (NewSource body, ServerContext ctx, ProbeHost probe, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Url)) return Results.BadRequest(new ApiError("indirizzo mancante"));
            string url = body.Url.Trim();
            if (ctx.Sources.Sources().Any(s => string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase)))
                return Results.Conflict(new ApiError("questa playlist c'è già"));
            M3uPlaylist pl;
            try { pl = M3uParser.Parse(await M3uParser.LoadTextAsync(url, ctx.Http, ct)); }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
            {
                return Results.BadRequest(new ApiError("playlist non leggibile: " + ex.Message));
            }
            if (pl.Entries.Count == 0) return Results.BadRequest(new ApiError("la playlist non contiene canali"));
            var now = ctx.Clock.GetUtcNow();
            string name = string.IsNullOrWhiteSpace(body.Name) ? DefaultName(url) : body.Name.Trim();
            long id = ctx.Sources.AddSource(name, url, now);
            var (added, removed, drm) = ctx.Sources.Import(id, pl, now);
            probe.Enqueue(id);
            return Results.Created($"/api/v1/sources/{id}", new ImportResult(id, pl.Entries.Count, added, removed, drm, pl.EpgUrl));
        })
            .WithSummary("Aggiunge una playlist (indirizzo http/https o file sul server), la importa e ne controlla i flussi")
            .Produces<ImportResult>(StatusCodes.Status201Created).Produces<ApiError>(StatusCodes.Status400BadRequest).Produces<ApiError>(StatusCodes.Status409Conflict);

        src.MapPatch("/{id:long}", IResult (long id, SourcePatch body, ServerContext ctx) =>
        {
            if (ctx.Sources.Source(id) is null) return Results.NotFound();
            ctx.Sources.UpdateSource(id, body.Name, body.MaxConnections, body.ClearMaxConnections == true, body.Enabled, body.Priority);
            return Results.Ok(ctx.Sources.Source(id));
        })
            .WithSummary("Modifica nome, limite di connessioni, attivazione o priorità di una playlist")
            .Produces<SourceRow>().Produces(StatusCodes.Status404NotFound);

        src.MapDelete("/{id:long}", IResult (long id, ServerContext ctx) =>
        {
            if (ctx.Sources.Source(id) is null) return Results.NotFound();
            ctx.Sources.DeleteSource(id);
            return Results.NoContent();
        })
            .WithSummary("Toglie una playlist e i suoi flussi (i canali restano)")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        src.MapPost("/{id:long}/refresh", async Task<IResult> (long id, ServerContext ctx, CancellationToken ct) =>
        {
            var s = ctx.Sources.Source(id);
            if (s is null) return Results.NotFound();
            try
            {
                var pl = M3uParser.Parse(await M3uParser.LoadTextAsync(s.Url, ctx.Http, ct));
                var (added, removed, drm) = ctx.Sources.Import(id, pl, ctx.Clock.GetUtcNow());
                return Results.Ok(new ImportResult(id, pl.Entries.Count, added, removed, drm, pl.EpgUrl));
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
            {
                ctx.Sources.SetSourceError(id, "playlist non raggiungibile: " + ex.Message, ctx.Clock.GetUtcNow());
                return Results.Json(new ApiError("playlist non raggiungibile: " + ex.Message), statusCode: StatusCodes.Status502BadGateway);
            }
        })
            .WithSummary("Rilegge la playlist")
            .Produces<ImportResult>().Produces(StatusCodes.Status404NotFound).Produces<ApiError>(StatusCodes.Status502BadGateway);

        src.MapPost("/{id:long}/check", IResult (long id, ServerContext ctx, ProbeHost probe) =>
        {
            if (ctx.Sources.Source(id) is null) return Results.NotFound();
            probe.Enqueue(id);
            return Results.Accepted();
        })
            .WithSummary("Controlla con ffprobe tutti i flussi della playlist, in background")
            .Produces(StatusCodes.Status202Accepted).Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------- canali e flussi
        var ch = api.MapGroup("/channels").WithTags("Canali");
        ch.MapGet("", (ServerContext ctx) => ctx.Sources.Channels()).WithSummary("Canali con il numero di flussi e di flussi funzionanti");
        ch.MapGet("/{id:long}/streams", (long id, ServerContext ctx) => ctx.Sources.Streams(channelId: id)).WithSummary("Flussi di un canale con l'esito dell'ultimo controllo");

        // ---------------------------------------------------------------- registrazioni
        var rec = api.MapGroup("/recordings").WithTags("Registrazioni");

        rec.MapGet("", (ServerContext ctx) => ctx.Recordings.List()).WithSummary("Tutte le registrazioni, le più recenti prima");

        rec.MapGet("/{id:long}", IResult (long id, ServerContext ctx) =>
            ctx.Recordings.Get(id) is { } r ? Results.Ok(new RecordingDetail(r, ctx.Recordings.Parts(id))) : Results.NotFound())
            .WithSummary("Una registrazione con i suoi spezzoni")
            .Produces<RecordingDetail>().Produces(StatusCodes.Status404NotFound);

        rec.MapPost("", IResult (NewRecording body, ServerContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(body.Title)) return Results.BadRequest(new ApiError("titolo mancante"));
            if (body.Stop <= body.Start) return Results.BadRequest(new ApiError("la fine deve essere dopo l'inizio"));
            if (!ctx.Sources.Channels().Any(c => c.Id == body.ChannelId)) return Results.BadRequest(new ApiError("canale sconosciuto"));
            int before = body.BeforeMin ?? ctx.Settings.GetInt("BeforeMin");
            int after = body.AfterMin ?? ctx.Settings.GetInt("AfterMin");
            string format = body.Format ?? ctx.Settings.Get("Format");
            if (before < 0 || after < 0) return Results.BadRequest(new ApiError("margini negativi"));
            if (format is not ("mp4" or "mp3")) return Results.BadRequest(new ApiError("formato: mp4 o mp3"));
            var now = ctx.Clock.GetUtcNow();
            if (body.Stop.AddMinutes(after) <= now) return Results.BadRequest(new ApiError("il programma è già finito"));
            var warnings = Warnings(ctx, body.Start.AddMinutes(-before), body.Stop.AddMinutes(after));
            long id = ctx.Recordings.Schedule(body.ChannelId, body.Title.Trim(), body.Start, body.Stop, before, after, format, now);
            return Results.Created($"/api/v1/recordings/{id}", new ScheduledRecording(id, warnings));
        })
            .WithSummary("Programma una registrazione (margini e formato predefiniti se mancano); risponde con gli avvisi")
            .Produces<ScheduledRecording>(StatusCodes.Status201Created).Produces<ApiError>(StatusCodes.Status400BadRequest);

        rec.MapPost("/{id:long}/stop", IResult (long id, ServerContext ctx) =>
            ctx.Recordings.Get(id) is null ? Results.NotFound()
            : ctx.Scheduler.Stop(id) ? Results.Accepted() : Results.Conflict(new ApiError("la registrazione non è in corso né in programma")))
            .WithSummary("Ferma una registrazione in corso (si unisce quello che c'è) o annulla una in programma")
            .Produces(StatusCodes.Status202Accepted).Produces(StatusCodes.Status404NotFound).Produces<ApiError>(StatusCodes.Status409Conflict);

        rec.MapDelete("/{id:long}", IResult (long id, ServerContext ctx) =>
            ctx.Recordings.Get(id) is null ? Results.NotFound()
            : ctx.Recordings.Cancel(id) ? Results.NoContent() : Results.Conflict(new ApiError("si annullano solo le registrazioni non ancora partite: per quelle in corso c'è stop")))
            .WithSummary("Annulla una registrazione non ancora partita")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces<ApiError>(StatusCodes.Status409Conflict);

        // ---------------------------------------------------------------- profili (aggiunta della wave 5)
        var prof = api.MapGroup("/profiles").WithTags("Profili");

        prof.MapGet("", (ServerContext ctx) => ctx.Profiles.List())
            .WithSummary("Profili della famiglia (gli adulti prima)");

        prof.MapPost("", IResult (NewProfile body, ServerContext ctx) =>
        {
            var (res, id) = ctx.Profiles.Create(body.Name, body.IsChild, body.Avatar, body.Pin, ctx.Clock.GetUtcNow());
            return res == ProfileResult.Ok ? Results.Created($"/api/v1/profiles/{id}", ctx.Profiles.Get(id)) : ProfileError(res);
        })
            .WithSummary("Crea un profilo con la sua lista «La mia serata in TV»; PIN facoltativo (4-8 cifre) solo per gli adulti")
            .Produces<ProfileRow>(StatusCodes.Status201Created).Produces<ApiError>(StatusCodes.Status400BadRequest).Produces<ApiError>(StatusCodes.Status409Conflict);

        prof.MapPatch("/{id:long}", IResult (long id, ProfilePatch body, ServerContext ctx) =>
        {
            var res = ctx.Profiles.Update(id, body.Name, body.IsChild, body.Avatar, body.Pin, body.ClearPin == true);
            return res == ProfileResult.Ok ? Results.Ok(ctx.Profiles.Get(id)) : ProfileError(res);
        })
            .WithSummary("Modifica nome, colore, tipo o PIN di un profilo")
            .Produces<ProfileRow>().Produces(StatusCodes.Status404NotFound).Produces<ApiError>(StatusCodes.Status400BadRequest).Produces<ApiError>(StatusCodes.Status409Conflict);

        prof.MapDelete("/{id:long}", IResult (long id, ServerContext ctx) =>
        {
            var res = ctx.Profiles.Delete(id);
            return res == ProfileResult.Ok ? Results.NoContent() : ProfileError(res);
        })
            .WithSummary("Elimina un profilo con le sue liste, preferenze e avvisi (non l'ultimo adulto)")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces<ApiError>(StatusCodes.Status409Conflict);

        prof.MapPost("/{id:long}/unlock", IResult (long id, PinCheck body, ServerContext ctx) =>
            ctx.Profiles.Unlock(id, body.Pin) switch
            {
                null => Results.NotFound(),
                true => Results.NoContent(),
                false => Results.Json(new ApiError("PIN sbagliato"), statusCode: StatusCodes.Status403Forbidden),
            })
            .WithSummary("Controlla il PIN di un profilo prima di entrarci")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces<ApiError>(StatusCodes.Status403Forbidden);

        // ---------------------------------------------------------------- backup
        var bak = api.MapGroup("/backups").WithTags("Sistema");
        bak.MapGet("", (ServerContext ctx) => ctx.Backups.List()).WithSummary("Copie del database (ogni notte alle 3, si tengono le ultime 7)");
        bak.MapPost("", (ServerContext ctx) => Results.Created("/api/v1/backups", ctx.Backups.Create(ctx.Clock.GetLocalNow())))
            .WithSummary("Fa subito una copia del database").Produces<BackupInfo>(StatusCodes.Status201Created);
    }

    /// <summary>Avvisi prima di programmare (da Get-ScheduleWarnings): troppe registrazioni sovrapposte, poco spazio.</summary>
    public static List<string> Warnings(ServerContext ctx, DateTimeOffset from, DateTimeOffset to)
    {
        var w = new List<string>();
        int max = ctx.Settings.GetInt("MaxParallel");
        int overlap = ctx.Recordings.Overlapping(from, to) + 1;
        if (max > 0 && overlap > max) w.Add($"{overlap} registrazioni contemporanee, il limite è {max}");
        try
        {
            double freeGb = new DriveInfo(Path.GetPathRoot(ctx.RecordingsDir)!).AvailableFreeSpace / 1e9;
            double needGb = (to - from).TotalHours * 2.5;
            int minFree = ctx.Settings.GetInt("MinFreeGB");
            if (freeGb - needGb < minFree) w.Add($"spazio libero {freeGb:0} GB: dopo questa registrazione (circa {needGb:0.0} GB) resterebbe sotto {minFree} GB");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
        return w;
    }

    static IResult ProfileError(ProfileResult r) => r switch
    {
        ProfileResult.NotFound => Results.NotFound(),
        ProfileResult.NameTaken => Results.Conflict(new ApiError("c'è già un profilo con questo nome")),
        ProfileResult.LastAdult => Results.Conflict(new ApiError("deve restare almeno un profilo adulto")),
        ProfileResult.InvalidPin => Results.BadRequest(new ApiError("PIN: da 4 a 8 cifre, solo per i profili adulti")),
        _ => Results.BadRequest(new ApiError("nome mancante o più lungo di 40 caratteri")),
    };

    static string DefaultName(string url)
    {
        string n = url.TrimEnd('/');
        int i = n.LastIndexOfAny(['/', '\\']);
        n = i >= 0 ? n[(i + 1)..] : n;
        return Path.GetFileNameWithoutExtension(n) is { Length: > 0 } s ? s : "Playlist";
    }
}
