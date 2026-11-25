using System.Collections.Concurrent;
using System.Net;
using DoveLoFanno.Data;

namespace DoveLoFanno.Server;

public sealed record AuthStatus(bool PasswordSet, bool LoggedIn, bool HomeNetwork, int MinLength);
public sealed record LoginBody(string? Password);
public sealed record PasswordBody(string? Current, string? Password);

/// <summary>
/// Accesso al server (24/11/2026). Regole:
/// - senza password: l'API risponde solo alla rete di casa (403 da fuori, anche dietro un proxy o un tunnel);
/// - con la password: ogni richiesta all'API vuole il cookie di accesso, da qualunque rete (401 senza).
/// Restano liberi l'interfaccia web (i file statici), lo stato del server, lo stato dell'accesso e il login.
/// </summary>
public static class ServerAuth
{
    public const string Cookie = "dlf_session";
    static readonly string[] Open = ["/api/v1/health", "/api/v1/auth/status", "/api/v1/auth/login", "/api/v1/openapi.json"];
    // tentativi sbagliati per indirizzo: dopo 5 si aspetta, sempre di più (fino a 5 minuti)
    static readonly ConcurrentDictionary<string, (int Fails, DateTimeOffset Until)> failures = new();

    /// <summary>Vero se la richiesta viene dalla rete di casa e non passa da un proxy o da un tunnel.</summary>
    public static bool FromHome(HttpContext http)
    {
        var h = http.Request.Headers;
        if (h.ContainsKey("X-Forwarded-For") || h.ContainsKey("Forwarded") || h.ContainsKey("CF-Connecting-IP") || h.ContainsKey("X-Real-IP"))
            return false;
        return AuthStore.IsHomeNetwork(http.Connection.RemoteIpAddress);
    }

    static bool LoggedIn(HttpContext http, AuthStore auth, DateTimeOffset now) =>
        auth.IsValid(http.Request.Cookies[Cookie], now);

    public static void UseServerAuth(this WebApplication app)
    {
        // password dimenticata: un file «reset-password» nella cartella dati (vale ovunque: Docker, servizio Windows)
        // oppure l'opzione DoveLoFanno:ResetPassword=true; all'avvio la password si toglie e il file si cancella
        var sc = app.Services.GetRequiredService<ServerContext>();
        string flag = Path.Combine(sc.DataDir, "reset-password");
        if (File.Exists(flag) || app.Configuration.GetValue<bool>("DoveLoFanno:ResetPassword"))
        {
            new AuthStore(sc.Settings).ClearPassword();
            try { File.Delete(flag); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            app.Logger.LogWarning("Password del server tolta all'avvio (reset-password): il server risponde solo dalla rete di casa.");
        }
        app.Use(async (http, next) =>
        {
            var ctx = http.RequestServices.GetRequiredService<ServerContext>();
            // l'indirizzo che i dispositivi di casa usano per aprire DoveLoFanno: serve ai link dei messaggi Telegram
            HomeAddress.Remember(ctx.Settings, FromHome(http), http.Connection.RemoteIpAddress, http.Request.Scheme, http.Request.Host.Value ?? "");
            string path = http.Request.Path.Value ?? "";
            if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || Open.Any(o => path.Equals(o, StringComparison.OrdinalIgnoreCase)))
            {
                await next();
                return;
            }
            var auth = new AuthStore(ctx.Settings);
            if (auth.HasPassword)
            {
                if (LoggedIn(http, auth, ctx.Clock.GetUtcNow())) { await next(); return; }
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await http.Response.WriteAsJsonAsync(new ApiError("Serve la password del server"));
                return;
            }
            if (FromHome(http)) { await next(); return; }
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            await http.Response.WriteAsJsonAsync(new ApiError("Senza password il server risponde solo dalla rete di casa: impostala in Impostazioni › Accesso"));
        });
    }

    static void SetCookie(HttpContext http, string token) =>
        http.Response.Cookies.Append(Cookie, token, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = http.Request.IsHttps, Path = "/",
            Expires = DateTimeOffset.UtcNow.Add(AuthStore.IdleLimit), IsEssential = true,
        });

    static string Device(HttpContext http)
    {
        string ua = http.Request.Headers.UserAgent.ToString();
        return (http.Connection.RemoteIpAddress?.ToString() ?? "locale") + (ua.Length > 0 ? " · " + ua : "");
    }

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/auth").WithTags("Accesso");

        g.MapGet("/status", (HttpContext http, ServerContext ctx) =>
        {
            var auth = new AuthStore(ctx.Settings);
            return new AuthStatus(auth.HasPassword, auth.HasPassword && LoggedIn(http, auth, ctx.Clock.GetUtcNow()), FromHome(http), AuthStore.MinLength);
        }).WithSummary("Se il server ha una password, se questo dispositivo ha fatto l'accesso e se è nella rete di casa");

        g.MapPost("/login", async Task<IResult> (LoginBody b, HttpContext http, ServerContext ctx) =>
        {
            var auth = new AuthStore(ctx.Settings);
            var now = ctx.Clock.GetUtcNow();
            string who = http.Connection.RemoteIpAddress?.ToString() ?? "locale";
            if (failures.TryGetValue(who, out var f) && f.Until > now)
                return Results.Json(new ApiError($"Troppi tentativi: riprova tra {Math.Max(1, (int)Math.Ceiling((f.Until - now).TotalSeconds))} secondi"), statusCode: StatusCodes.Status429TooManyRequests);
            if (!auth.HasPassword) return Results.BadRequest(new ApiError("Il server non ha una password"));
            if (!auth.Check(b.Password))
            {
                await Task.Delay(500);
                int n = (failures.TryGetValue(who, out var old) ? old.Fails : 0) + 1;
                var wait = n >= 5 ? TimeSpan.FromSeconds(Math.Min(300, 15 * Math.Pow(2, n - 5))) : TimeSpan.Zero;
                failures[who] = (n, now + wait);
                return Results.Json(new ApiError("Password sbagliata"), statusCode: StatusCodes.Status401Unauthorized);
            }
            failures.TryRemove(who, out _);
            SetCookie(http, auth.CreateSession(Device(http), now));
            return Results.NoContent();
        }).WithSummary("Accesso con la password del server: imposta il cookie del dispositivo")
          .Produces(StatusCodes.Status204NoContent).Produces<ApiError>(StatusCodes.Status401Unauthorized).Produces<ApiError>(StatusCodes.Status429TooManyRequests);

        g.MapPost("/logout", (HttpContext http, ServerContext ctx) =>
        {
            new AuthStore(ctx.Settings).DeleteSession(http.Request.Cookies[Cookie]);
            http.Response.Cookies.Delete(Cookie, new CookieOptions { Path = "/" });
            return Results.NoContent();
        }).WithSummary("Esce da questo dispositivo").Produces(StatusCodes.Status204NoContent);

        g.MapPut("/password", IResult (PasswordBody b, HttpContext http, ServerContext ctx) =>
        {
            var auth = new AuthStore(ctx.Settings);
            if (auth.HasPassword && !auth.Check(b.Current)) return Results.BadRequest(new ApiError("La password attuale non è giusta"));
            if (AuthStore.Validate(b.Password) is { Length: > 0 } why) return Results.BadRequest(new ApiError(why));
            auth.SetPassword(b.Password!.Trim());
            SetCookie(http, auth.CreateSession(Device(http), ctx.Clock.GetUtcNow()));    // chi la imposta resta dentro
            return Results.NoContent();
        }).WithSummary("Imposta o cambia la password del server; gli altri dispositivi devono rifare l'accesso")
          .Produces(StatusCodes.Status204NoContent).Produces<ApiError>(StatusCodes.Status400BadRequest);

        g.MapPost("/password/remove", IResult (LoginBody b, HttpContext http, ServerContext ctx) =>
        {
            var auth = new AuthStore(ctx.Settings);
            if (!auth.HasPassword) return Results.NoContent();
            if (!auth.Check(b.Password)) return Results.BadRequest(new ApiError("La password attuale non è giusta"));
            if (!FromHome(http)) return Results.BadRequest(new ApiError("La password si può togliere solo dalla rete di casa"));
            auth.ClearPassword();
            http.Response.Cookies.Delete(Cookie, new CookieOptions { Path = "/" });
            return Results.NoContent();
        }).WithSummary("Toglie la password (solo dalla rete di casa): il server torna a rispondere solo alla rete di casa")
          .Produces(StatusCodes.Status204NoContent).Produces<ApiError>(StatusCodes.Status400BadRequest);
    }
}
