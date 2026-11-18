using DoveLoFanno.Data;

namespace DoveLoFanno.Server;

public sealed record PreferenceBody(string Name, string? Text, string? Group, string? Sub, double? MinRating, bool? Enabled);
public sealed record GroupsBody(IReadOnlyList<string> Groups);
public sealed record ReadBody(long UpTo);
public sealed record ServicesBody(IReadOnlyList<string> Services);
public sealed record FamilySubscriptions(IReadOnlyList<string> Services, IReadOnlyList<string> Known);
public sealed record ProfileSubscriptionsBody(bool Inherits, IReadOnlyList<string>? Services);
public sealed record GeneralSettings(int BeforeMin, int AfterMin, string Format, int MaxParallel, int MinFreeGb, bool SetupDone, string RecordingsDir, double? FreeGb);
public sealed record GeneralSettingsPatch(int? BeforeMin, int? AfterMin, string? Format, int? MaxParallel, int? MinFreeGb, bool? SetupDone);
public sealed record NotificationSettings(bool TelegramConfigured, string TelegramChat, string QuietFrom, string QuietTo);
public sealed record NotificationSettingsPatch(string? TelegramToken, string? TelegramChat, bool? ClearTelegram, string? QuietFrom, string? QuietTo);

/// <summary>
/// Preferenze e proposte, "non proporre più", avvisi, notifiche, abbonamenti, impostazioni generali e anteprima live.
/// Aggiunte compatibili all'API v1 del 18/11/2026.
/// </summary>
public static class PreferencesApi
{
    public static void Map(RouteGroupBuilder api)
    {
        var prof = api.MapGroup("/profiles").WithTags("Preferenze");

        // ---------------------------------------------------------------- preferenze e proposte
        prof.MapGet("/{id:long}/preferences", IResult (long id, ServerContext ctx) =>
            ctx.Preferences.List(id) is { } l ? Results.Ok(l) : Results.NotFound())
            .WithSummary("Preferenze del profilo: cosa cercare nella guida per le proposte")
            .Produces<List<PreferenceRow>>().Produces(StatusCodes.Status404NotFound);

        prof.MapPost("/{id:long}/preferences", IResult (long id, PreferenceBody b, ServerContext ctx, NotifyHost notify) =>
        {
            string why = PreferenceStore.Validate(b.Name ?? "", b.Text ?? "", b.Group ?? "", b.MinRating ?? 0);
            if (why.Length > 0) return Results.BadRequest(new ApiError(why));
            long? pid = ctx.Preferences.Add(id, b.Name!, b.Text ?? "", b.Group ?? "", b.Sub ?? "", b.MinRating ?? 0, b.Enabled ?? true);
            if (pid == null) return Results.NotFound();
            notify.ProposalsChanged();
            return Results.Created($"/api/v1/profiles/{id}/preferences/{pid}", ctx.Preferences.List(id)!.First(p => p.Id == pid));
        })
            .WithSummary("Aggiunge una preferenza (testo anche su regia e attori, genere, sottogenere, voto minimo)")
            .Produces<PreferenceRow>(StatusCodes.Status201Created).Produces<ApiError>(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound);

        prof.MapPut("/{id:long}/preferences/{prefId:long}", IResult (long id, long prefId, PreferenceBody b, ServerContext ctx, NotifyHost notify) =>
        {
            string why = PreferenceStore.Validate(b.Name ?? "", b.Text ?? "", b.Group ?? "", b.MinRating ?? 0);
            if (why.Length > 0) return Results.BadRequest(new ApiError(why));
            if (!ctx.Preferences.Update(id, prefId, b.Name!, b.Text ?? "", b.Group ?? "", b.Sub ?? "", b.MinRating ?? 0, b.Enabled ?? true)) return Results.NotFound();
            notify.ProposalsChanged();
            return Results.Ok(ctx.Preferences.List(id)!.First(p => p.Id == prefId));
        })
            .WithSummary("Modifica una preferenza o la attiva e disattiva")
            .Produces<PreferenceRow>().Produces<ApiError>(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound);

        prof.MapDelete("/{id:long}/preferences/{prefId:long}", IResult (long id, long prefId, ServerContext ctx) =>
            ctx.Preferences.Delete(id, prefId) ? Results.NoContent() : Results.NotFound())
            .WithSummary("Toglie una preferenza").Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        prof.MapGet("/{id:long}/excluded-groups", IResult (long id, ServerContext ctx) =>
            ctx.Preferences.ExcludedGroups(id) is { } g ? Results.Ok(new GroupsBody(g)) : Results.NotFound())
            .WithSummary("Generi che il profilo non vuole mai tra le proposte").Produces<GroupsBody>().Produces(StatusCodes.Status404NotFound);

        prof.MapPut("/{id:long}/excluded-groups", IResult (long id, GroupsBody b, ServerContext ctx, NotifyHost notify) =>
        {
            if (!ctx.Preferences.SetExcludedGroups(id, b.Groups ?? [])) return Results.NotFound();
            notify.ProposalsChanged();
            return Results.Ok(new GroupsBody(ctx.Preferences.ExcludedGroups(id)!));
        })
            .WithSummary("Cambia i generi esclusi dalle proposte").Produces<GroupsBody>().Produces(StatusCodes.Status404NotFound);

        prof.MapGet("/{id:long}/proposals", IResult (long id, int? days, ServerContext ctx) =>
            ctx.Preferences.Proposals(id, ctx.Clock.GetUtcNow(), days ?? 7, ctx.Settings.GetInt("MaxParallel"), ctx.Settings.GetInt("BeforeMin"), ctx.Settings.GetInt("AfterMin")) is { } l
                ? Results.Ok(l) : Results.NotFound())
            .WithSummary("Proposte per te: un passaggio per opera, nei prossimi giorni (7 se non indicato, al massimo 14)")
            .Produces<List<Proposal>>().Produces(StatusCodes.Status404NotFound);

        prof.MapGet("/{id:long}/ignored", IResult (long id, ServerContext ctx) =>
            ctx.Preferences.Ignored(id) is { } l ? Results.Ok(l) : Results.NotFound())
            .WithSummary("Opere da non proporre più").Produces<List<IgnoredWork>>().Produces(StatusCodes.Status404NotFound);

        prof.MapPost("/{id:long}/ignored", IResult (long id, WorkRef b, ServerContext ctx) =>
            ctx.Preferences.Ignore(id, b.WorkId, ctx.Clock.GetUtcNow()) ? Results.NoContent() : Results.NotFound())
            .WithSummary("Non proporre più un'opera").Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        prof.MapDelete("/{id:long}/ignored", IResult (long id, string key, ServerContext ctx) =>
            ctx.Preferences.Unignore(id, key) ? Results.NoContent() : Results.NotFound())
            .WithSummary("Torna a proporre un'opera (chiave dell'opera)").Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------- avvisi
        prof.MapGet("/{id:long}/alerts", IResult (long id, long? workId, ServerContext ctx) =>
            ctx.Alerts.List(id, workId, ctx.Clock.GetUtcNow()) is { } l ? Results.Ok(l) : Results.NotFound())
            .WithSummary("Avvisi del profilo (di un'opera sola con workId), con il loro stato")
            .Produces<List<AlertRow>>().Produces(StatusCodes.Status404NotFound);

        prof.MapPut("/{id:long}/alerts", IResult (long id, AlertBody b, ServerContext ctx) =>
        {
            string why = AlertStore.Validate(b);
            if (why.Length > 0) return Results.BadRequest(new ApiError(why));
            long? aid = ctx.Alerts.Save(id, b);
            if (aid == null) return Results.NotFound();
            return Results.Ok(ctx.Alerts.List(id, b.WorkId, ctx.Clock.GetUtcNow())!.First(a => a.Id == aid));
        })
            .WithSummary("Crea o modifica l'avviso di un'opera: in chiaro in TV (tv), nei miei abbonamenti (subscription), a noleggio o acquisto (rent_buy)")
            .Produces<AlertRow>().Produces<ApiError>(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound);

        prof.MapDelete("/{id:long}/alerts/{alertId:long}", IResult (long id, long alertId, ServerContext ctx) =>
            ctx.Alerts.Delete(id, alertId) ? Results.NoContent() : Results.NotFound())
            .WithSummary("Toglie un avviso").Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------- notifiche
        prof.MapGet("/{id:long}/notifications", IResult (long id, ServerContext ctx) =>
            ctx.Notifications.List(id) is { } l ? Results.Ok(l) : Results.NotFound())
            .WithSummary("Ultime notifiche del profilo e della famiglia, con quante non sono lette")
            .Produces<NotificationList>().Produces(StatusCodes.Status404NotFound);

        prof.MapPost("/{id:long}/notifications/read", IResult (long id, ReadBody b, ServerContext ctx) =>
            ctx.Notifications.MarkRead(id, b.UpTo) ? Results.NoContent() : Results.NotFound())
            .WithSummary("Segna come lette le notifiche fino a quella indicata").Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        // ---------------------------------------------------------------- abbonamenti
        prof.MapGet("/{id:long}/subscriptions", IResult (long id, ServerContext ctx) =>
            ctx.Subscriptions.OfProfile(id) is { } s ? Results.Ok(s) : Results.NotFound())
            .WithSummary("Abbonamenti del profilo: ereditati dalla famiglia o suoi").Produces<ProfileSubscriptions>().Produces(StatusCodes.Status404NotFound);

        prof.MapPut("/{id:long}/subscriptions", IResult (long id, ProfileSubscriptionsBody b, ServerContext ctx) =>
            ctx.Subscriptions.SetProfile(id, b.Inherits, b.Services) ? Results.Ok(ctx.Subscriptions.OfProfile(id)) : Results.NotFound())
            .WithSummary("Eredita gli abbonamenti della famiglia o tiene una scelta sua").Produces<ProfileSubscriptions>().Produces(StatusCodes.Status404NotFound);

        var sub = api.MapGroup("/subscriptions").WithTags("Preferenze");
        sub.MapGet("", (ServerContext ctx) => new FamilySubscriptions(ctx.Subscriptions.Family(), ctx.Subscriptions.Known()))
            .WithSummary("Abbonamenti della famiglia e piattaforme che si possono scegliere");
        sub.MapPut("", (ServicesBody b, ServerContext ctx) =>
        {
            ctx.Subscriptions.SetFamily(b.Services ?? []);
            return new FamilySubscriptions(ctx.Subscriptions.Family(), ctx.Subscriptions.Known());
        })
            .WithSummary("Cambia gli abbonamenti della famiglia (valgono per i profili che li ereditano)");

        api.MapGet("/guide/genres", (ServerContext ctx) => ctx.Preferences.SubGenres(ctx.Clock.GetUtcNow()))
            .WithTags("Guida").WithSummary("Sottogeneri presenti nella guida da adesso in poi (almeno 3 programmi)");

        // ---------------------------------------------------------------- impostazioni generali e notifiche
        var set = api.MapGroup("/settings").WithTags("Impostazioni");
        set.MapGet("/general", (ServerContext ctx) => General(ctx))
            .WithSummary("Margini e formato predefiniti, limiti, primo avvio fatto, cartella e spazio libero delle registrazioni");
        set.MapPut("/general", IResult (GeneralSettingsPatch b, ServerContext ctx) =>
        {
            if (b.BeforeMin is < 0 or > 60 || b.AfterMin is < 0 or > 120) return Results.BadRequest(new ApiError("margini: da 0 a 60 minuti prima, da 0 a 120 dopo"));
            if (b.Format is not (null or "mp4" or "mp3")) return Results.BadRequest(new ApiError("formato: mp4 o mp3"));
            if (b.MaxParallel is < 0 or > 20) return Results.BadRequest(new ApiError("registrazioni contemporanee: da 0 (nessun limite) a 20"));
            if (b.MinFreeGb is < 0 or > 10000) return Results.BadRequest(new ApiError("spazio minimo non valido"));
            if (b.BeforeMin is int bm) ctx.Settings.Set("BeforeMin", bm.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (b.AfterMin is int am) ctx.Settings.Set("AfterMin", am.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (b.Format is string f) ctx.Settings.Set("Format", f);
            if (b.MaxParallel is int mp) ctx.Settings.Set("MaxParallel", mp.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (b.MinFreeGb is int mf) ctx.Settings.Set("MinFreeGB", mf.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (b.SetupDone is bool sd) ctx.Settings.Set("SetupDone", sd ? "1" : "0");
            return Results.Ok(General(ctx));
        })
            .WithSummary("Cambia margini, formato, limiti o segna fatto il primo avvio")
            .Produces<GeneralSettings>().Produces<ApiError>(StatusCodes.Status400BadRequest);

        set.MapGet("/notifications", (ServerContext ctx) => NotificationInfo(ctx))
            .WithSummary("Telegram (solo se è configurato, mai il token) e orari di silenzio");
        set.MapPut("/notifications", async Task<IResult> (NotificationSettingsPatch b, ServerContext ctx, CancellationToken ct) =>
        {
            foreach (string? t in new[] { b.QuietFrom, b.QuietTo })
                if (t is { Length: > 0 } && !TimeOnly.TryParse(t, System.Globalization.CultureInfo.InvariantCulture, out _))
                    return Results.BadRequest(new ApiError("orari di silenzio nel formato 23:00"));
            if (b.ClearTelegram == true) { ctx.Settings.Set("TelegramToken", ""); ctx.Settings.Set("TelegramChat", ""); }
            else if (!string.IsNullOrWhiteSpace(b.TelegramToken) || !string.IsNullOrWhiteSpace(b.TelegramChat))
            {
                string token = string.IsNullOrWhiteSpace(b.TelegramToken) ? ctx.Settings.Get("TelegramToken") : b.TelegramToken.Trim();
                string chat = string.IsNullOrWhiteSpace(b.TelegramChat) ? ctx.Settings.Get("TelegramChat") : b.TelegramChat.Trim();
                if (token.Length == 0 || chat.Length == 0) return Results.BadRequest(new ApiError("servono il token del bot e la chat"));
                try { await Telegram.SendAsync(ctx.Http, token, chat, "DoveLoFanno è collegato: qui arriveranno avvisi e registrazioni.", ct); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    return Results.BadRequest(new ApiError("Telegram non ha accettato il messaggio di prova: controlla token e chat (" + ex.Message + ")"));
                }
                ctx.Settings.Set("TelegramToken", token);
                ctx.Settings.Set("TelegramChat", chat);
            }
            if (b.QuietFrom != null) ctx.Settings.Set("QuietFrom", b.QuietFrom.Trim());
            if (b.QuietTo != null) ctx.Settings.Set("QuietTo", b.QuietTo.Trim());
            return Results.Ok(NotificationInfo(ctx));
        })
            .WithSummary("Collega Telegram (manda un messaggio di prova prima di salvare) o lo scollega; orari di silenzio")
            .Produces<NotificationSettings>().Produces<ApiError>(StatusCodes.Status400BadRequest);

        // ---------------------------------------------------------------- anteprima live
        api.MapGet("/channels/{id:long}/preview", async Task<IResult> (long id, ServerContext ctx, PreviewService preview, HttpContext http, CancellationToken ct) =>
        {
            if (!ctx.Sources.Channels().Any(c => c.Id == id)) return Results.NotFound();
            var (jpeg, why) = await preview.GetAsync(id, ct);
            if (jpeg == null) return Results.Json(new ApiError(why), statusCode: StatusCodes.Status503ServiceUnavailable);
            http.Response.Headers.CacheControl = "no-store";
            return Results.File(jpeg, "image/jpeg");
        })
            .WithTags("Canali").WithSummary("Anteprima live muta: un fotogramma JPEG del canale (si rinnova ogni 8 secondi)")
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg").Produces(StatusCodes.Status404NotFound).Produces<ApiError>(StatusCodes.Status503ServiceUnavailable);
    }

    static GeneralSettings General(ServerContext ctx)
    {
        double? free = null;
        try { free = Math.Round(new DriveInfo(Path.GetPathRoot(ctx.RecordingsDir)!).AvailableFreeSpace / 1e9, 1); }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
        return new GeneralSettings(ctx.Settings.GetInt("BeforeMin"), ctx.Settings.GetInt("AfterMin"), ctx.Settings.Get("Format"),
            ctx.Settings.GetInt("MaxParallel"), ctx.Settings.GetInt("MinFreeGB"), ctx.Settings.Get("SetupDone") == "1", ctx.RecordingsDir, free);
    }

    static NotificationSettings NotificationInfo(ServerContext ctx) =>
        new(ctx.Settings.Get("TelegramToken").Length > 0 && ctx.Settings.Get("TelegramChat").Length > 0, ctx.Settings.Get("TelegramChat"),
            ctx.Settings.Get("QuietFrom"), ctx.Settings.Get("QuietTo"));
}
