using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoveLoFanno.Data;
using DoveLoFanno.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

/// <summary>Password del server e «solo rete di casa» (24/11/2026).</summary>
public sealed class AuthTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-auth-" + Guid.NewGuid().ToString("N"));
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    public AuthTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void PasswordEGettoni()
    {
        var auth = new AuthStore(new SettingsStore(Path.Combine(dir, "dlf.db")));
        Assert.False(auth.HasPassword);
        Assert.False(auth.Check("qualcosa"));
        Assert.NotEmpty(AuthStore.Validate("corta"));

        auth.SetPassword("divano rosso 42");
        Assert.True(auth.HasPassword);
        Assert.True(auth.Check("divano rosso 42"));
        Assert.False(auth.Check("divano rosso 43"));

        string t = auth.CreateSession("prova", Now);
        Assert.True(auth.IsValid(t, Now.AddDays(10)));
        Assert.False(auth.IsValid(t + "x", Now));
        Assert.False(auth.IsValid(t, Now.AddDays(10).Add(AuthStore.IdleLimit).AddDays(1)));     // non usato da troppo tempo (ultimo uso: +10 giorni)

        auth.SetPassword("un'altra password");                                         // cambiata: tutti fuori
        Assert.False(auth.IsValid(t, Now));
        string t2 = auth.CreateSession("prova", Now);
        auth.DeleteSession(t2);
        Assert.False(auth.IsValid(t2, Now));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("192.168.1.10", true)]
    [InlineData("10.4.5.6", true)]
    [InlineData("172.20.0.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::1", true)]
    [InlineData("fd00::5", true)]
    [InlineData("2a01:4f8::1", false)]
    [InlineData("::ffff:192.168.1.5", true)]
    public void ReteDiCasa(string ip, bool home) => Assert.Equal(home, AuthStore.IsHomeNetwork(IPAddress.Parse(ip)));

    [Fact]
    public async Task LApiVuoleLaPasswordQuandoCe()
    {
        var tools = HlsSimulator.FindTools();
        if (tools == null) return;                // come gli altri test dell'API: serve ffmpeg
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DoveLoFanno:DataDir", dir);
            b.UseSetting("DoveLoFanno:Ffmpeg", tools.FfmpegPath);
        });
        var mine = factory.CreateClient();

        // senza password: dalla rete di casa sì, attraverso un proxy no
        Assert.Equal(HttpStatusCode.OK, (await mine.GetAsync("/api/v1/profiles")).StatusCode);
        var proxied = new HttpRequestMessage(HttpMethod.Get, "/api/v1/profiles");
        proxied.Headers.Add("X-Forwarded-For", "203.0.113.7");
        Assert.Equal(HttpStatusCode.Forbidden, (await mine.SendAsync(proxied)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await mine.PutAsJsonAsync("/api/v1/auth/password", new PasswordBody(null, "corta"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mine.PutAsJsonAsync("/api/v1/auth/password", new PasswordBody(null, "divano rosso 42"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mine.GetAsync("/api/v1/profiles")).StatusCode);       // chi l'ha impostata resta dentro

        var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/v1/profiles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/v1/health")).StatusCode);
        var st = await other.GetFromJsonAsync<AuthStatus>("/api/v1/auth/status", Json);
        Assert.True(st!.PasswordSet);
        Assert.False(st.LoggedIn);

        Assert.Equal(HttpStatusCode.Unauthorized, (await other.PostAsJsonAsync("/api/v1/auth/login", new LoginBody("sbagliata"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsJsonAsync("/api/v1/auth/login", new LoginBody("divano rosso 42"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/v1/profiles")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/v1/profiles")).StatusCode);

        // togliere la password vuole quella attuale; poi si torna a «solo rete di casa»
        Assert.Equal(HttpStatusCode.BadRequest, (await mine.PostAsJsonAsync("/api/v1/auth/password/remove", new LoginBody("sbagliata"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mine.PostAsJsonAsync("/api/v1/auth/password/remove", new LoginBody("divano rosso 42"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/v1/profiles")).StatusCode);
    }
}
