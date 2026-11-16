using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoveLoFanno.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace DoveLoFanno.Tests;

/// <summary>
/// Collaudo del server da capo a fondo, attraverso l'API: playlist aggiunta, canali, registrazione programmata
/// che parte da sola, finisce e lascia l'MP4. Usa l'HLS simulato e ffmpeg vero.
/// </summary>
[Collection("ffmpeg")]
public sealed class ServerTests(ITestOutputHelper output) : IAsyncLifetime
{
    readonly string data = Path.Combine(Path.GetTempPath(), "dlf-srv-" + Guid.NewGuid().ToString("N"));
    HlsSimulator? sim;
    WebApplicationFactory<Program>? factory;

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync()
    {
        var tools = HlsSimulator.FindTools();
        if (tools == null) return;
        sim = await HlsSimulator.StartAsync(tools);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DoveLoFanno:DataDir", data);
            b.UseSetting("DoveLoFanno:Ffmpeg", tools.FfmpegPath);
        });
    }

    public async Task DisposeAsync()
    {
        if (factory != null) await factory.DisposeAsync();
        if (sim != null) await sim.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(data, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task DallaPlaylistAllaRegistrazioneFinita()
    {
        if (sim == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }
        var http = factory!.CreateClient();

        var health = await http.GetFromJsonAsync<JsonElement>("/api/v1/health");
        Assert.Equal("v1", health.GetProperty("api").GetString());

        // playlist locale con un canale che passa dal "relinker" e uno DRM
        Directory.CreateDirectory(data);
        string m3u = Path.Combine(data, "prova.m3u");
        await File.WriteAllTextAsync(m3u, $"""
            #EXTM3U
            #EXTINF:-1 tvg-id="Prova.it" group-title="Test",Canale di prova
            {sim.BaseUrl}/relinker
            #EXTINF:-1 tvg-id="Drm.it",Canale DRM
            #KODIPROP:inputstream.adaptive.license_key=https://lic.example/
            {sim.BaseUrl}/missing
            """);
        var add = await http.PostAsJsonAsync("/api/v1/sources", new NewSource("Prova", m3u));
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        var imported = await add.Content.ReadFromJsonAsync<ImportResult>(Json);
        Assert.Equal((2, 2, 1), (imported!.Channels, imported.Added, imported.Drm));
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/v1/sources", new NewSource("", m3u))).StatusCode);

        var channels = await http.GetFromJsonAsync<JsonElement>("/api/v1/channels");
        long ch = channels.EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Canale di prova").GetProperty("id").GetInt64();
        long drm = channels.EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Canale DRM").GetProperty("id").GetInt64();

        var now = DateTimeOffset.Now;
        var bad = await http.PostAsJsonAsync("/api/v1/recordings", new NewRecording(ch, "Prova", now, now.AddSeconds(-5), 0, 0, null));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var created = await http.PostAsJsonAsync("/api/v1/recordings", new NewRecording(ch, "Prova API", now, now.AddSeconds(15), 0, 0, "mp4"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        long id = (await created.Content.ReadFromJsonAsync<ScheduledRecording>(Json))!.Id;
        // un canale con il solo flusso DRM non si può registrare: la registrazione fallisce con il motivo
        long idDrm = (await (await http.PostAsJsonAsync("/api/v1/recordings", new NewRecording(drm, "DRM", now, now.AddSeconds(10), 0, 0, "mp4")))
            .Content.ReadFromJsonAsync<ScheduledRecording>(Json))!.Id;

        JsonElement rec = default;
        var until = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < until)
        {
            rec = (await http.GetFromJsonAsync<JsonElement>($"/api/v1/recordings/{id}")).GetProperty("recording");
            if (rec.GetProperty("state").GetString() is not ("scheduled" or "recording" or "merging")) break;
            await Task.Delay(1000);
        }
        output.WriteLine(rec.ToString());
        Assert.Equal("completed", rec.GetProperty("state").GetString());
        string file = rec.GetProperty("file").GetString()!;
        Assert.True(File.Exists(file));
        Assert.StartsWith(Path.Combine(data, "registrazioni"), file);
        var detail = await http.GetFromJsonAsync<JsonElement>($"/api/v1/recordings/{id}");
        Assert.NotEmpty(detail.GetProperty("parts").EnumerateArray());

        var recDrm = (await http.GetFromJsonAsync<JsonElement>($"/api/v1/recordings/{idDrm}")).GetProperty("recording");
        Assert.Equal("failed", recDrm.GetProperty("state").GetString());
        Assert.Contains("nessun flusso", recDrm.GetProperty("log").GetString());

        // annullare una registrazione futura; una finita non si annulla
        var later = await (await http.PostAsJsonAsync("/api/v1/recordings", new NewRecording(ch, "Domani", now.AddDays(1), now.AddDays(1).AddHours(1), null, null, null)))
            .Content.ReadFromJsonAsync<ScheduledRecording>(Json);
        Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"/api/v1/recordings/{later!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await http.DeleteAsync($"/api/v1/recordings/{id}")).StatusCode);
    }
}
