using DoveLoFanno.Data;

namespace DoveLoFanno.Server;

/// <summary>Crea una regola «Registra la serie» dal programma da cui si parte (o dall'opera), per il profilo indicato.</summary>
public sealed record NewSeriesRule(long? ProgrammeId, long? WorkId, long? ProfileId);

/// <summary>
/// «Registra la serie» (20/11/2026): una regola per titolo programma subito le puntate in guida, una volta per puntata
/// (le repliche si saltano), e poi da sola quelle che la guida riporta più avanti (NotifyHost, ogni mezz'ora).
/// </summary>
public static class SeriesApi
{
    public static SeriesDefaults Defaults(ServerContext ctx) =>
        new(ctx.Settings.GetInt("BeforeMin"), ctx.Settings.GetInt("AfterMin"), ctx.Settings.Get("Format"), ctx.Settings.GetInt("MaxParallel"));

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/series").WithTags("Registrazioni");

        g.MapGet("", (ServerContext ctx) => ctx.Series.Rules(ctx.Clock.GetUtcNow()))
            .WithSummary("Le serie in registrazione: puntate in programma e registrate, la prossima");

        g.MapPost("", IResult (NewSeriesRule b, ServerContext ctx) =>
        {
            long? workId = b.WorkId;
            if (b.ProgrammeId is long pid)
            {
                if (ctx.Guide.Programme(pid) is not { } p) return Results.BadRequest(new ApiError("programma sconosciuto"));
                workId = p.WorkId;
            }
            if (workId is not long w) return Results.BadRequest(new ApiError("serve il programma o l'opera"));
            var now = ctx.Clock.GetUtcNow();
            if (ctx.Series.Create(b.ProfileId, w, now) is not long id)
                return Results.BadRequest(new ApiError("si registrano tutte le puntate solo di serie e programmi, non dei film"));
            return ctx.Series.Check(id, now, Defaults(ctx)) is { } check ? Results.Ok(check) : Results.NotFound();
        })
            .WithSummary("Registra la serie: crea la regola (o ritrova quella attiva) e programma le puntate in guida; " +
                "risponde con le puntate trovate e i numeri di episodio che mancano")
            .Produces<SeriesCheck>().Produces<ApiError>(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound);

        g.MapPost("/{id:long}/check", IResult (long id, ServerContext ctx) =>
            ctx.Series.Check(id, ctx.Clock.GetUtcNow(), Defaults(ctx)) is { } check ? Results.Ok(check) : Results.NotFound())
            .WithSummary("Ricontrolla la guida per una serie e programma le puntate nuove")
            .Produces<SeriesCheck>().Produces(StatusCodes.Status404NotFound);

        g.MapDelete("/{id:long}", IResult (long id, ServerContext ctx) =>
            ctx.Series.Stop(id) ? Results.NoContent() : Results.NotFound())
            .WithSummary("Smette di registrare la serie e annulla le sue registrazioni non ancora partite")
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);
    }
}
