using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoveLoFanno.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace DoveLoFanno.Tests;

/// <summary>Guida attraverso il server: la playlist indica la guida, il server la carica da solo e la espone nell'API.</summary>
public sealed class GuideApiTests(ITestOutputHelper output) : IAsyncLifetime
{
    readonly string data = Path.Combine(Path.GetTempPath(), "dlf-gapi-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task InitializeAsync()
    {
        var tools = HlsSimulator.FindTools();
        if (tools == null) return Task.CompletedTask;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DoveLoFanno:DataDir", data);
            b.UseSetting("DoveLoFanno:Ffmpeg", tools.FfmpegPath);
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (factory != null) await factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(data, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task DallaPlaylistAllaGuidaEAllaRegistrazione()
    {
        if (factory == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }
        var http = factory.CreateClient();
        Directory.CreateDirectory(data);
        var now = DateTimeOffset.UtcNow;
        string xml = Path.Combine(data, "guida.xml");
        await File.WriteAllTextAsync(xml, GuideStoreTests.Xmltv(now));
        string m3u = Path.Combine(data, "prova.m3u");
        await File.WriteAllTextAsync(m3u, $"""
            #EXTM3U x-tvg-url="{xml}"
            #EXTINF:-1 tvg-id="Canale5.it",Canale 5
            http://127.0.0.1:9/c5.m3u8
            """);
        Assert.Equal(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/v1/sources", new NewSource("Prova", m3u))).StatusCode);

        // la guida si carica in background
        JsonElement status = default;
        var until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until)
        {
            status = await http.GetFromJsonAsync<JsonElement>("/api/v1/guide/status");
            if (status.GetProperty("programmes").GetInt32() > 0) break;
            await Task.Delay(300);
        }
        output.WriteLine(status.ToString());
        Assert.Equal(4, status.GetProperty("programmes").GetInt32());
        Assert.Equal(1, status.GetProperty("linkedChannels").GetInt32());

        string q = $"/api/v1/guide?from={Uri.EscapeDataString(now.ToString("o"))}&to={Uri.EscapeDataString(now.AddHours(6).ToString("o"))}";
        var progs = await http.GetFromJsonAsync<JsonElement>(q + "&onlyRecordable=true");
        Assert.Equal(2, progs.GetArrayLength());
        var film = progs.EnumerateArray().Single(p => p.GetProperty("title").GetString() == "Il Padrino");
        Assert.False(film.GetProperty("scheduled").GetBoolean());
        Assert.Single((await http.GetFromJsonAsync<JsonElement>(q + "&text=coppola")).EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync($"/api/v1/guide?from={Uri.EscapeDataString(now.ToString("o"))}&to={Uri.EscapeDataString(now.AddDays(40).ToString("o"))}")).StatusCode);

        // Registra dal programma: la registrazione si lega a programma e opera, e il programma risulta in programma
        var rec = new NewRecording(film.GetProperty("channelId").GetInt64(), "Il Padrino",
            film.GetProperty("start").GetDateTimeOffset(), film.GetProperty("stop").GetDateTimeOffset(), null, null, null,
            film.GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/v1/recordings", rec)).StatusCode);
        var again = await http.GetFromJsonAsync<JsonElement>(q);
        Assert.True(again.EnumerateArray().Single(p => p.GetProperty("title").GetString() == "Il Padrino").GetProperty("scheduled").GetBoolean());
        var list = await http.GetFromJsonAsync<JsonElement>("/api/v1/recordings");
        Assert.True(list[0].GetProperty("workId").ValueKind == JsonValueKind.Number);

        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/v1/recordings", rec with { ProgrammeId = 999999 })).StatusCode);

        // la matita (23/11/2026): si cambiano titolo, orari, margini e formato di una registrazione non partita
        long recId = list[0].GetProperty("id").GetInt64();
        var edit = rec with { Title = "Il Padrino (versione lunga)", Stop = rec.Stop.AddMinutes(20), BeforeMin = 5, AfterMin = 15, Format = "mp3", ProgrammeId = null };
        Assert.Equal(HttpStatusCode.OK, (await http.PutAsJsonAsync($"/api/v1/recordings/{recId}", edit)).StatusCode);
        var edited = (await http.GetFromJsonAsync<JsonElement>($"/api/v1/recordings/{recId}")).GetProperty("recording");
        Assert.Equal(("Il Padrino (versione lunga)", 5, 15, "mp3"), (edited.GetProperty("title").GetString(), edited.GetProperty("beforeMin").GetInt32(),
            edited.GetProperty("afterMin").GetInt32(), edited.GetProperty("format").GetString()));
        Assert.Equal(rec.Stop.AddMinutes(20), edited.GetProperty("stop").GetDateTimeOffset());
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsJsonAsync($"/api/v1/recordings/{recId}", edit with { Stop = edit.Start })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PutAsJsonAsync("/api/v1/recordings/999999", edit)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"/api/v1/recordings/{recId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await http.PutAsJsonAsync($"/api/v1/recordings/{recId}", edit)).StatusCode);   // annullata: non si modifica

        // filtro «Canali» (23/11/2026): regione della famiglia e scelta a mano per canale
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsJsonAsync("/api/v1/settings/general", new { region = "Atlantide" })).StatusCode);
        var gen = await (await http.PutAsJsonAsync("/api/v1/settings/general", new { region = "Lombardia" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Lombardia", gen.GetProperty("region").GetString());
        Assert.Equal(20, gen.GetProperty("regions").GetArrayLength());
        long chId = film.GetProperty("channelId").GetInt64();
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsJsonAsync("/api/v1/channels/guide", new { ids = new[] { chId }, show = "forse" })).StatusCode);
        var changed = await (await http.PutAsJsonAsync("/api/v1/channels/guide", new { ids = new[] { chId, 999999L }, show = "hide" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, changed.GetProperty("changed").GetInt32());
        var chs = await http.GetFromJsonAsync<JsonElement>("/api/v1/channels");
        Assert.False(chs.EnumerateArray().Single(c => c.GetProperty("id").GetInt64() == chId).GetProperty("guideShow").GetBoolean());
    }
}
