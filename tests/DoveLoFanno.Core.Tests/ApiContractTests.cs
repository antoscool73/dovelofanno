using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DoveLoFanno.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace DoveLoFanno.Tests;

/// <summary>
/// API v1 congelata: il documento OpenAPI che il server produce deve coincidere con docs/api/openapi-v1.json.
/// Se cambia: un'aggiunta compatibile si approva aggiornando il file nello stesso commit (la CI salva quello nuovo
/// come artefatto "openapi-v1"); un cambiamento incompatibile è la v2.
/// </summary>
public sealed class ApiContractTests(ITestOutputHelper output) : IAsyncLifetime
{
    readonly string data = Path.Combine(Path.GetTempPath(), "dlf-api-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program>? factory;

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

    static string? RepoFile(string relative)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "DoveLoFanno.slnx"))) return Path.Combine(d.FullName, relative);
        return null;
    }

    static string Normalize(string json) =>
        JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            .ReplaceLineEndings("\n");

    [Fact]
    public async Task DocumentoOpenApiCongelato()
    {
        if (factory == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }
        string actual = Normalize(await factory.CreateClient().GetStringAsync("/api/v1/openapi.json"));
        string? snapshotOut = Environment.GetEnvironmentVariable("DLF_API_SNAPSHOT_OUT");
        if (!string.IsNullOrEmpty(snapshotOut)) await File.WriteAllTextAsync(snapshotOut, actual);

        string path = RepoFile(Path.Combine("docs", "api", "openapi-v1.json"))!;
        Assert.True(File.Exists(path), "manca docs/api/openapi-v1.json: prenderlo dall'artefatto openapi-v1 della CI");
        string expected = Normalize(await File.ReadAllTextAsync(path));
        if (expected != actual)
        {
            var e = expected.Split('\n');
            var a = actual.Split('\n');
            int i = 0;
            while (i < Math.Min(e.Length, a.Length) && e[i] == a[i]) i++;
            output.WriteLine($"Prima differenza alla riga {i + 1}:\n  salvato:  {(i < e.Length ? e[i] : "(fine)")}\n  prodotto: {(i < a.Length ? a[i] : "(fine)")}");
        }
        Assert.True(expected == actual, "l'API v1 è cambiata rispetto a docs/api/openapi-v1.json (vedi l'output del test)");
    }

    // 20/11/2026: le sezioni predefinite di La mia lista che il profilo nasconde (Preferenze › La mia lista)
    [Fact]
    public async Task SezioniNascosteDellaHomePerProfilo()
    {
        if (factory == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }
        var http = factory.CreateClient();
        var prof = await (await http.PostAsJsonAsync("/api/v1/profiles", new { name = "Sezioni", isChild = false })).Content.ReadFromJsonAsync<JsonElement>();
        long id = prof.GetProperty("id").GetInt64();
        Assert.Equal(0, (await http.GetFromJsonAsync<JsonElement>($"/api/v1/profiles/{id}/home")).GetProperty("hidden").GetArrayLength());
        var put = await http.PutAsJsonAsync($"/api/v1/profiles/{id}/home", new { hidden = new[] { "settimana", "continua", "settimana" } });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var got = await http.GetFromJsonAsync<JsonElement>($"/api/v1/profiles/{id}/home");
        Assert.Equal(["settimana", "continua"], got.GetProperty("hidden").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsJsonAsync($"/api/v1/profiles/{id}/home", new { hidden = new[] { "serata" } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/profiles/999999/home")).StatusCode);
    }

    [Fact]
    public async Task BackupDalloApi()
    {
        if (factory == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }
        var http = factory.CreateClient();
        // all'avvio il server ne fa già uno (nessun backup recente)
        var created = await http.PostAsync("/api/v1/backups", null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var list = await http.GetFromJsonAsync<JsonElement>("/api/v1/backups");
        Assert.True(list.GetArrayLength() >= 1);
        string name = list[0].GetProperty("name").GetString()!;
        string file = Path.Combine(data, "backup", name);
        Assert.True(File.Exists(file));
        // la copia è un database valido, alla versione attuale
        using var c = new SqliteConnection($"Data Source={file};Pooling=False");
        c.Open();
        Assert.Equal(Database.LatestVersion, Database.UserVersion(c));
    }

    [Fact]
    public async Task ImpostazioniDeiVoti_ChiaveMaiRestituitaEImdbSpentoDiBase()
    {
        if (factory == null) { output.WriteLine("ffmpeg non trovato: test saltato"); return; }
        var http = factory.CreateClient();
        var s = await http.GetFromJsonAsync<JsonElement>("/api/v1/settings/ratings");
        Assert.False(s.GetProperty("hasTmdbKey").GetBoolean());
        Assert.False(s.GetProperty("imdbEnabled").GetBoolean());          // licenza IMDb solo personale: spento di base
        var on = await http.PutAsJsonAsync("/api/v1/settings/ratings", new { imdbEnabled = true });
        Assert.True((await on.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("imdbEnabled").GetBoolean());
        Assert.DoesNotContain("tmdbKey\"", (await http.GetStringAsync("/api/v1/settings/ratings")).Replace("hasTmdbKey\"", ""));
    }

    [Fact]
    public void BackupSiTengonoLeUltimeSette()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dlf-bak-" + Guid.NewGuid().ToString("N"));
        try
        {
            string db = Path.Combine(dir, "dlf.db");
            Database.Open(db).Dispose();
            var b = new Backup(db, Path.Combine(dir, "backup"));
            var t = new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero);
            for (int i = 0; i < 10; i++) b.Create(t.AddDays(i));
            var list = b.List();
            Assert.Equal(7, list.Count);
            Assert.Equal("dlf-20261110-030000.db", list[0].Name);
            Assert.Equal("dlf-20261104-030000.db", list[^1].Name);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, true);
        }
    }
}
