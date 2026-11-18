using System.Text.Json;
using DoveLoFanno.Ratings;

namespace DoveLoFanno.Tests;

public sealed class PlatformCatalogTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-plat-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Theory]
    [InlineData("Cine Comico Amazon Channel", "Amazon Prime Video", "Cine Comico")]
    [InlineData("iWonder Full Amazon channel", "Amazon Prime Video", "iWonder Full")]
    [InlineData("Paramount Plus Apple TV channel", "Apple TV", "Paramount Plus")]
    [InlineData("Amazon Prime Video with Ads", "Amazon Prime Video", "Amazon Prime Video with Ads")]
    [InlineData("Netflix basic with Ads", "Netflix", "Netflix basic with Ads")]
    [InlineData("Netflix", "Netflix", "Netflix")]
    public void GruppoENomeBreve(string name, string group, string shortName) =>
        Assert.Equal((group, shortName), PlatformCatalog.Classify(name));

    [Fact]
    public void OrdineDallaPiuDiffusaConICanaliSottoLaLoroPiattaforma()
    {
        string file = Path.Combine(dir, "platforms-IT.json");
        Directory.CreateDirectory(dir);
        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            Fetched = now.AddDays(-1),
            Items = new[]
            {
                new PlatformEntry("Netflix", "/n.png", 0),
                new PlatformEntry("Amazon Prime Video", "/a.png", 1),
                new PlatformEntry("Disney Plus", "/d.png", 3),
                new PlatformEntry("Cine Comico Amazon Channel", "/c.png", 47),
                new PlatformEntry("Infinity Selection Amazon Channel", "/i.png", 42),
            },
        }));
        var cat = new PlatformCatalog(file);
        Assert.False(cat.Stale(now));
        Assert.True(cat.Stale(now.AddDays(7)));

        var all = cat.All(["Netflix", "DAZN", "Servizio sconosciuto"]);
        Assert.Equal(
            ["Netflix", "Amazon Prime Video", "Infinity Selection Amazon Channel", "Cine Comico Amazon Channel", "Disney Plus", "DAZN", "Servizio sconosciuto"],
            all.Select(p => p.Name));
        Assert.Equal("https://image.tmdb.org/t/p/w92/c.png", all.Single(p => p.Short == "Cine Comico").Logo);
        Assert.Equal("", all.Single(p => p.Name == "DAZN").Logo);       // senza logo nel catalogo: il client mostra l'iniziale
    }

    [Fact]
    public void SenzaCatalogoRestanoINomiNellOrdineDiRiserva()
    {
        var cat = new PlatformCatalog(Path.Combine(dir, "manca.json"));
        Assert.True(cat.Stale(DateTimeOffset.UtcNow));
        Assert.Equal(["Netflix", "Disney Plus", "Rai Play"], cat.All(["Rai Play", "Disney Plus", "Netflix"]).Select(p => p.Name));
    }
}
