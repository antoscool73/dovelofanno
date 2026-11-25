using System.Net;
using DoveLoFanno.Data;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

/// <summary>Indirizzo di casa per i link dei messaggi Telegram, e locandina e link nella coda (25/11/2026).</summary>
public sealed class HomeAddressTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-home-" + Guid.NewGuid().ToString("N"));
    static readonly IPAddress Phone = IPAddress.Parse("192.168.1.50");
    static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Rome");

    public HomeAddressTests()
    {
        Directory.CreateDirectory(dir);
        HomeAddress.ResetCache();
    }

    public void Dispose()
    {
        HomeAddress.ResetCache();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    SettingsStore Settings() => new(Path.Combine(dir, "dlf.db"));

    [Theory]
    [InlineData("http", "192.168.1.10:8765", "http://192.168.1.10:8765")]
    [InlineData("http", "10.0.0.5", "http://10.0.0.5")]
    [InlineData("https", "un62.local:8765", "https://un62.local:8765")]
    [InlineData("http", "localhost:8765", null)]
    [InlineData("http", "127.0.0.1:8765", null)]
    [InlineData("http", "[::1]:8765", null)]
    [InlineData("http", "172.29.29.183:8765", null)]       // rete interna di WSL o Docker: dal telefono non si raggiunge
    [InlineData("http", "100.101.102.103:8765", null)]     // Tailscale: non è la rete di casa
    [InlineData("http", "8.8.8.8", null)]
    [InlineData("ftp", "192.168.1.10", null)]
    public void QualiIndirizziValgono(string scheme, string host, string? expected) =>
        Assert.Equal(expected, HomeAddress.Candidate(scheme, host));

    [Fact]
    public void ImparaDaiDispositiviDiCasa()
    {
        var s = Settings();
        Assert.False(HomeAddress.Remember(s, fromHome: false, Phone, "http", "192.168.1.10:8765"));       // da fuori o da un tunnel
        Assert.False(HomeAddress.Remember(s, true, IPAddress.Loopback, "http", "192.168.1.10:8765"));    // browser sul server stesso
        Assert.Equal("", HomeAddress.Get(s));

        Assert.True(HomeAddress.Remember(s, true, Phone, "http", "192.168.1.10:8765"));
        Assert.False(HomeAddress.Remember(s, true, Phone, "http", "192.168.1.10:8765"));                 // già salvato
        Assert.Equal("http://192.168.1.10:8765", HomeAddress.Get(s));

        Assert.False(HomeAddress.Remember(s, true, Phone, "http", "un62.local:8765"));                   // un nome non toglie il numero
        Assert.True(HomeAddress.Remember(s, true, Phone, "http", "192.168.1.11:8765"));                  // il server ha cambiato indirizzo
        Assert.Equal("http://192.168.1.11:8765", Settings().Get(HomeAddress.Key));
    }

    [Fact]
    public void UnNomeValeSeNonCeUnNumero()
    {
        var s = Settings();
        Assert.True(HomeAddress.Remember(s, true, Phone, "http", "un62.local:8765"));
        Assert.True(HomeAddress.Remember(s, true, Phone, "http", "192.168.1.10:8765"));
        Assert.Equal("http://192.168.1.10:8765", HomeAddress.Get(s));
    }

    [Fact]
    public void LaCodaTieneLocandinaELinkEMetteIlinkNellElenco()
    {
        var s = Settings();
        var t = new DateTimeOffset(2026, 11, 25, 21, 0, 0, TimeSpan.Zero);
        TelegramQueue.Add(s, [new QueuedMessage(t, "Registrata: «Lei»\nLA7 Cinema", "https://image.tmdb.org/t/p/w500/x.jpg", "http://192.168.1.10:8765/opera/12"),
            new QueuedMessage(t.AddMinutes(5), "È uscita la versione 0.4.0")]);
        var items = TelegramQueue.TakeAll(s);
        Assert.Equal("https://image.tmdb.org/t/p/w500/x.jpg", items[0].Photo);
        Assert.Equal("http://192.168.1.10:8765/opera/12", items[0].Link);
        Assert.Equal(("", ""), (items[1].Photo, items[1].Link));

        string text = TelegramQueue.Format(items, Rome);
        Assert.Contains("22:00 · Registrata: «Lei»\nLA7 Cinema\nhttp://192.168.1.10:8765/opera/12", text);
        Assert.DoesNotContain("image.tmdb.org", text);
    }

    [Fact]
    public void LaCodaVecchiaSenzaLocandinaSiLeggeAncora()
    {
        var s = Settings();
        s.Set("TelegramQueue", """[{"at":"2026-11-24T22:15:00+00:00","text":"Registrata: «X»"}]""");
        var m = Assert.Single(TelegramQueue.TakeAll(s));
        Assert.Equal(("Registrata: «X»", "", ""), (m.Text, m.Photo, m.Link));
    }
}
