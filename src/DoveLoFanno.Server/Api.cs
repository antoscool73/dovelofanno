using DoveLoFanno.Data;
using DoveLoFanno.Sources;

namespace DoveLoFanno.Server;

public sealed record NewSource(string Name, string Url);
public sealed record SourcePatch(string? Name, int? MaxConnections, bool? ClearMaxConnections, bool? Enabled, int? Priority);
public sealed record NewRecording(long ChannelId, string Title, DateTimeOffset Start, DateTimeOffset Stop, int? BeforeMin, int? AfterMin, string? Format);
public sealed record ScheduledRecording(long Id, IReadOnlyList<string> Warnings);
public sealed record ImportResult(long SourceId, int Channels, int Added, int Removed, int Drm, string EpgUrl);

/// <summary>
/// API v1 minima della wave 2: sorgenti, canali e flussi, registrazioni. Si congela nella wave 3; da lì ogni
/// cambiamento è una versione nuova o un'aggiunta compatibile.
/// </summary>
public static class Api
{
    public const string Version = "v1";

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapGet("/health", (ServerContext ctx) => Results.Ok(new
        {
            status = "ok",
            api = Version,
            database = Database.LatestVersion,
            running = ctx.Scheduler.Running.Count,
        }));

        // ---------------------------------------------------------------- sorgenti
        api.MapGet("/sources", (ServerContext ctx) => ctx.Sources.Sources());

        api.MapPost("/sources", async (NewSource body, ServerContext ctx, ProbeHost probe, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Url)) return Results.BadRequest(new { error = "indirizzo mancante" });
            if (ctx.Sources.Sources().Any(s => string.Equals(s.Url, body.Url.Trim(), StringComparison.OrdinalIgnoreCase)))
                return Results.Conflict(new { error = "questa playlist c'è già" });
            M3uPlaylist pl;
            try { pl = M3uParser.Parse(await M3uParser.LoadTextAsync(body.Url.Trim(), ctx.Http, ct)); }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
            {
                return Results.BadRequest(new { error = "playlist non leggibile: " + ex.Message });
            }
            if (pl.Entries.Count == 0) return Results.BadRequest(new { error = "la playlist non contiene canali" });
            var now = ctx.Clock.GetUtcNow();
            string name = string.IsNullOrWhiteSpace(body.Name) ? DefaultName(body.Url) : body.Name.Trim();
            long id = ctx.Sources.AddSource(name, body.Url.Trim(), now);
            var (added, removed, drm) = ctx.Sources.Import(id, pl, now);
            probe.Enqueue(id);
            return Results.Created($"/api/v1/sources/{id}", new ImportResult(id, pl.Entries.Count, added, removed, drm, pl.EpgUrl));
        });

        api.MapPatch("/sources/{id:long}", (long id, SourcePatch body, ServerContext ctx) =>
        {
            if (ctx.Sources.Source(id) is null) return Results.NotFound();
            ctx.Sources.UpdateSource(id, body.Name, body.MaxConnections, body.ClearMaxConnections == true, body.Enabled, body.Priority);
            return Results.Ok(ctx.Sources.Source(id));
        });

        api.MapDelete("/sources/{id:long}", (long id, ServerContext ctx) =>
        {
            if (ctx.Sources.Source(id) is null) return Results.NotFound();
            ctx.Sources.DeleteSource(id);
            return Results.NoContent();
        });

        api.MapPost("/sources/{id:long}/refresh", async (long id, ServerContext ctx, CancellationToken ct) =>
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
                return Results.Problem("playlist non raggiungibile: " + ex.Message, statusCode: 502);
            }
        });

        api.MapPost("/sources/{id:long}/check", (long id, ServerContext ctx, ProbeHost probe) =>
        {
            if (ctx.Sources.Source(id) is null) return Results.NotFound();
            probe.Enqueue(id);
            return Results.Accepted();
        });

        // ---------------------------------------------------------------- canali e flussi
        api.MapGet("/channels", (ServerContext ctx) => ctx.Sources.Channels());
        api.MapGet("/channels/{id:long}/streams", (long id, ServerContext ctx) => ctx.Sources.Streams(channelId: id));

        // ---------------------------------------------------------------- registrazioni
        api.MapGet("/recordings", (ServerContext ctx) => ctx.Recordings.List());

        api.MapGet("/recordings/{id:long}", (long id, ServerContext ctx) =>
            ctx.Recordings.Get(id) is { } r ? Results.Ok(new { recording = r, parts = ctx.Recordings.Parts(id) }) : Results.NotFound());

        api.MapPost("/recordings", (NewRecording body, ServerContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(body.Title)) return Results.BadRequest(new { error = "titolo mancante" });
            if (body.Stop <= body.Start) return Results.BadRequest(new { error = "la fine deve essere dopo l'inizio" });
            if (!ctx.Sources.Channels().Any(c => c.Id == body.ChannelId)) return Results.BadRequest(new { error = "canale sconosciuto" });
            int before = body.BeforeMin ?? ctx.Settings.GetInt("BeforeMin");
            int after = body.AfterMin ?? ctx.Settings.GetInt("AfterMin");
            string format = body.Format ?? ctx.Settings.Get("Format");
            if (before < 0 || after < 0) return Results.BadRequest(new { error = "margini negativi" });
            if (format is not ("mp4" or "mp3")) return Results.BadRequest(new { error = "formato: mp4 o mp3" });
            var now = ctx.Clock.GetUtcNow();
            if (body.Stop.AddMinutes(after) <= now) return Results.BadRequest(new { error = "il programma è già finito" });
            var warnings = Warnings(ctx, body.Start.AddMinutes(-before), body.Stop.AddMinutes(after));
            long id = ctx.Recordings.Schedule(body.ChannelId, body.Title.Trim(), body.Start, body.Stop, before, after, format, now);
            return Results.Created($"/api/v1/recordings/{id}", new ScheduledRecording(id, warnings));
        });

        api.MapPost("/recordings/{id:long}/stop", (long id, ServerContext ctx) =>
            ctx.Recordings.Get(id) is null ? Results.NotFound()
            : ctx.Scheduler.Stop(id) ? Results.Accepted() : Results.Conflict(new { error = "la registrazione non è in corso né in programma" }));

        api.MapDelete("/recordings/{id:long}", (long id, ServerContext ctx) =>
            ctx.Recordings.Get(id) is null ? Results.NotFound()
            : ctx.Recordings.Cancel(id) ? Results.NoContent() : Results.Conflict(new { error = "si annullano solo le registrazioni non ancora partite: per quelle in corso c'è stop" }));
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

    static string DefaultName(string url)
    {
        string n = url.TrimEnd('/');
        int i = n.LastIndexOfAny(['/', '\\']);
        n = i >= 0 ? n[(i + 1)..] : n;
        return Path.GetFileNameWithoutExtension(n) is { Length: > 0 } s ? s : "Playlist";
    }
}
