using DoveLoFanno.Data;
using DoveLoFanno.Media;

namespace DoveLoFanno.Server;

/// <summary>Converti una registrazione (RecordingId) o un file della libreria (LibraryItemId) in mp4 o mp3.</summary>
public sealed record NewConversion(long? RecordingId, long? LibraryItemId, string Format, long? ProfileId);

/// <summary>
/// «Converti...» (20/11/2026): si convertono solo file che DoveLoFanno conosce (registrazioni e file della libreria), non
/// percorsi qualsiasi. ConversionHost le fa una alla volta e avvisa alla fine con una notifica.
/// </summary>
public static class ConversionApi
{
    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/conversions").WithTags("Registrazioni");

        g.MapGet("", (ServerContext ctx) => ctx.Conversions.List())
            .WithSummary("Le ultime conversioni, le più recenti prima");

        g.MapPost("", IResult (NewConversion b, ServerContext ctx, ConversionHost host) =>
        {
            if (b.Format is not ("mp4" or "mp3")) return Results.BadRequest(new ApiError("formato: mp4 o mp3"));
            string? source = null, title = null;
            if (b.RecordingId is long rid)
            {
                var r = ctx.Recordings.Get(rid);
                if (r == null) return Results.NotFound();
                source = r.File;
                title = r.Title;
            }
            else if (b.LibraryItemId is long iid)
            {
                source = ctx.Library.ItemPath(iid);
                if (source == null) return Results.NotFound();
                title = Path.GetFileNameWithoutExtension(source);
            }
            if (source == null) return Results.BadRequest(new ApiError("serve la registrazione o il file della libreria"));
            if (source.Length == 0 || !File.Exists(source)) return Results.BadRequest(new ApiError("il file non c'è (registrazione non riuscita o file spostato)"));
            // 23/11/2026 (Antonio): convertire nello stesso formato non serve, e un MP3 è solo audio: non diventa un video
            string ext = Path.GetExtension(source).TrimStart('.').ToLowerInvariant();
            if (ext == b.Format) return Results.BadRequest(new ApiError($"è già un file {b.Format.ToUpperInvariant()}"));
            if (ext == "mp3") return Results.BadRequest(new ApiError("un MP3 è solo audio: non si converte in video"));
            long id = ctx.Conversions.Add(b.ProfileId, source, title!, b.Format, ctx.Clock.GetUtcNow());
            host.Wake();
            return Results.Created($"/api/v1/conversions/{id}", ctx.Conversions.Get(id));
        })
            .WithSummary("Mette in coda una conversione in MP4 o MP3; il file nuovo va accanto all'originale, che resta")
            .Produces<ConversionRow>(StatusCodes.Status201Created).Produces<ApiError>(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound);

        g.MapDelete("/{id:long}", IResult (long id, ServerContext ctx) =>
            ctx.Conversions.Get(id) is null ? Results.NotFound()
            : ctx.Conversions.Cancel(id) ? Results.NoContent() : Results.Conflict(new ApiError("si annullano solo le conversioni ancora in coda")))
            .WithSummary("Annulla una conversione ancora in coda")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces<ApiError>(StatusCodes.Status409Conflict);
    }
}

/// <summary>Le conversioni in coda, una alla volta, con una notifica alla fine.</summary>
public sealed class ConversionHost(ServerContext ctx, ILogger<ConversionHost> log) : BackgroundService
{
    readonly SemaphoreSlim wake = new(0);

    public void Wake()
    {
        if (wake.CurrentCount == 0) wake.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        int again = ctx.Conversions.Requeue();
        if (again > 0) log.LogWarning("Conversioni riprese dopo il riavvio: {Count}", again);
        while (!ct.IsCancellationRequested)
        {
            ConversionRow? next;
            while ((next = ctx.Conversions.TakeNext()) != null)
            {
                string logBase = Path.Combine(ctx.LogDir, $"conv-{next.Id}");
                ConversionOutcome o;
                try
                {
                    o = await Converter.ConvertAsync(ctx.Tools, next.Source, next.Format, Path.Combine(ctx.RecordingsDir, "Convertiti"), logBase, ct);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { o = new ConversionOutcome(false, "", "", ex.Message); }
                var now = ctx.Clock.GetUtcNow();
                ctx.Conversions.Finish(next.Id, o.Ok, o.Output, o.Method, o.Error, logBase, now);
                string what = next.Format == "mp3" ? "in MP3" : "in MP4";
                if (o.Ok) ctx.Notifications.Add(next.ProfileId, "recording", $"Convertito {what}: «{next.Title}»", Path.GetFileName(o.Output), "/impostazioni#registrazioni", now);
                else ctx.Notifications.Add(next.ProfileId, "recording", $"Conversione non riuscita: «{next.Title}»", o.Error, "/impostazioni#registrazioni", now);
                log.LogInformation("Conversione {Id} {Title}: {Result}", next.Id, next.Title, o.Ok ? o.Method : o.Error);
            }
            try { await wake.WaitAsync(TimeSpan.FromSeconds(30), ct); } catch (OperationCanceledException) { return; }
        }
    }
}
