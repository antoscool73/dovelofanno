using System.Net.Http.Json;
using System.Text.Json;
using DoveLoFanno.Server;
using DoveLoFanno.Updates;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace DoveLoFanno.Tests;

/// <summary>Controllo degli aggiornamenti: confronto delle versioni, version.json, API e notifica una volta sola.</summary>
public sealed class UpdateTests(ITestOutputHelper output) : IAsyncLifetime
{
    readonly string data = Path.Combine(Path.GetTempPath(), "dlf-upd-" + Guid.NewGuid().ToString("N"));
    string Feed => Path.Combine(data, "version.json");
    WebApplicationFactory<Program>? factory;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(data);
        var tools = HlsSimulator.FindTools();
        if (tools == null) return Task.CompletedTask;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DoveLoFanno:DataDir", data);
            b.UseSetting("DoveLoFanno:Ffmpeg", tools.FfmpegPath);
            b.UseSetting("DoveLoFanno:UpdateFeed", Feed);
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (factory != null) await factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(data, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1.0", "0.2.0", false)]
    [InlineData("0.10.0", "0.9.0", true)]          // numeri, non testo
    [InlineData("1.0.0", "0.99.99", true)]
    [InlineData("0.2.0", "0.2.0-dev", true)]        // il rilascio è più nuovo della compilazione di prova
    [InlineData("0.2.0-dev", "0.2.0", false)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.1", true)]
    [InlineData("1.0.0-beta.10", "1.0.0-beta.9", true)]
    [InlineData("1.0.0-beta", "1.0.0-alpha.5", true)]
    [InlineData("1.0.0-beta.1", "1.0.0-beta", true)]
    [InlineData("0.2.0", "0.1.0+abc123", true)]
    [InlineData("v0.2.0", "0.1.0", false)]          // il file deve dire la versione senza «v»
    [InlineData("0.2", "0.1.0", false)]
    [InlineData("rotto", "0.1.0", false)]
    public void ConfrontoDelleVersioni(string candidate, string current, bool newer) =>
        Assert.Equal(newer, Releases.IsNewer(candidate, current));

    [Fact]
    public void LetturaDiVersionJson()
    {
        var r = Releases.Parse("""{"version":"0.2.0","date":"2026-12-01","notes":"Registra la serie","url":"https://dovelofanno.it/beta/"}""");
        Assert.Equal(new ReleaseInfo("0.2.0", "2026-12-01", "Registra la serie", "https://dovelofanno.it/beta/"), r);
        Assert.Equal("", Releases.Parse("""{"version":"0.2.0","date":"ieri","url":"https://x.it/"}""").Date);
        Assert.Throws<FormatException>(() => Releases.Parse("""{"version":"0.2","url":"https://x.it/"}"""));
        Assert.Throws<FormatException>(() => Releases.Parse("""{"version":"0.2.0","url":"http://x.it/"}"""));      // solo https
        Assert.Throws<FormatException>(() => Releases.Parse("""{"version":"0.2.0"}"""));
        Assert.ThrowsAny<JsonException>(() => Releases.Parse("<html>"));
    }

    [Fact]
    public void VersioneDelProgramma() => Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", Releases.Current);

    [Fact]
    public async Task ControlloNotificaUnaVoltaSolaAgliAdulti()
    {
        if (factory == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }
        var http = factory.CreateClient();
        long anna = await Profile(http, "Anna", false);
        long bimbi = await Profile(http, "Bambini", true);

        var s = await http.GetFromJsonAsync<UpdateStatus>("/api/v1/settings/updates", Json);
        Assert.True(s!.Enabled);
        Assert.Null(s.Latest);
        Assert.Equal(Releases.Current, s.Current);

        // sito irraggiungibile: errore, nessuna notifica
        s = await Check(http);
        Assert.NotEqual("", s.Error);
        Assert.NotNull(s.CheckedAt);

        await File.WriteAllTextAsync(Feed, """{"version":"999.0.0","date":"2026-12-01","notes":"Novità","url":"https://dovelofanno.it/beta/"}""");
        s = await Check(http);
        Assert.Equal(("", "999.0.0", true, "Novità"), (s.Error, s.Latest, s.UpdateAvailable, s.Notes));
        await Check(http);                                         // stesso numero: niente seconda notifica
        Assert.Equal(1, await Count(http, anna));
        Assert.Equal(0, await Count(http, bimbi));

        // una versione vecchia non è un aggiornamento
        await File.WriteAllTextAsync(Feed, """{"version":"0.0.1","url":"https://dovelofanno.it/beta/"}""");
        s = await Check(http);
        Assert.False(s.UpdateAvailable);
        Assert.Equal(1, await Count(http, anna));

        var off = await http.PutAsJsonAsync("/api/v1/settings/updates", new UpdateSettingsPatch(false));
        Assert.False((await off.Content.ReadFromJsonAsync<UpdateStatus>(Json))!.Enabled);
    }

    static async Task<UpdateStatus> Check(HttpClient http)
    {
        var res = await http.PostAsync("/api/v1/settings/updates/check", null);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<UpdateStatus>(Json))!;
    }

    static async Task<long> Profile(HttpClient http, string name, bool child)
    {
        var res = await http.PostAsJsonAsync("/api/v1/profiles", new NewProfile(name, child, null, null));
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
    }

    static async Task<int> Count(HttpClient http, long profile)
    {
        var list = await http.GetFromJsonAsync<JsonElement>($"/api/v1/profiles/{profile}/notifications");
        return list.GetProperty("items").EnumerateArray().Count(n => n.GetProperty("title").GetString()!.StartsWith("È uscita"));
    }
}
