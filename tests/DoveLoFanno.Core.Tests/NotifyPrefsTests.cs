using DoveLoFanno.Data;
using DoveLoFanno.Server;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

/// <summary>Notifiche a scelta per tipo e canale di notifica e proposte con i dati del film (26/11/2026).</summary>
public sealed class NotifyPrefsTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-notif-" + Guid.NewGuid().ToString("N"));
    string Db => Path.Combine(dir, "dlf.db");
    static readonly DateTimeOffset Now = new(2026, 11, 26, 15, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public void SenzaSceltaArrivaTutto()
    {
        var s = new SettingsStore(Db);
        long anna = new ProfileStore(Db).Create("Anna", false, null, null, Now).Id;
        var prefs = NotifyPrefs.Load(s, anna);
        Assert.Equal(NotifyPrefs.Kinds, prefs.Select(p => p.Kind));
        Assert.All(prefs, p => Assert.True(p.Web && p.Telegram));
        Assert.Equal(["recording_problem", "alert"], prefs.Where(p => p.WebLocked).Select(p => p.Kind));
    }

    [Fact]
    public void SceltePerTipoECanale()
    {
        var s = new SettingsStore(Db);
        var profiles = new ProfileStore(Db);
        long anna = profiles.Create("Anna", false, null, null, Now).Id;
        long bruno = profiles.Create("Bruno", false, null, null, Now).Id;
        var saved = NotifyPrefs.Save(s, anna, [
            new NotifyPref("proposals", false, false, false),
            new NotifyPref("alert", false, true, false),          // gli avvisi restano nella campanella
            new NotifyPref("recording", true, false, false),
        ]);
        Assert.False(saved.Single(p => p.Kind == "proposals").Web);
        Assert.True(saved.Single(p => p.Kind == "alert").Web);
        Assert.False(NotifyPrefs.Telegram(s, anna, "recording"));
        Assert.True(NotifyPrefs.Telegram(s, anna, "alert"));
        Assert.True(NotifyPrefs.Telegram(s, anna, "vecchio-tipo"));
        Assert.Equal(["proposals"], NotifyPrefs.HiddenInApp(s, anna));

        // la campanella di Anna non mostra le proposte, quella di Bruno sì
        var n = new NotificationStore(Db);
        n.Add(anna, "proposals", "Proposta per te: «Il Padrino»", "", "/opera/1", Now);
        n.Add(null, "recording", "Registrata: «Geo»", "", "/registrazioni", Now);
        n.Add(bruno, "proposals", "Proposta per te: «Roma»", "", "/opera/2", Now);
        var lista = n.List(anna, hidden: NotifyPrefs.HiddenInApp(s, anna))!;
        Assert.Equal(["Registrata: «Geo»"], lista.Items.Select(i => i.Title));
        Assert.Equal(1, lista.Unread);
        Assert.Equal(2, n.List(bruno, hidden: NotifyPrefs.HiddenInApp(s, bruno))!.Items.Count);
    }

    [Fact]
    public void TestoDellaProposta()
    {
        var h = new SearchHit(7, "film", "Le ali della libertà", "", 1994, "", 9.3, "IMDb", 278, "movie", "", Now.AddHours(5.2), false, 0, 0, [],
            Genres: ["Dramma", "Crimine", "Storia"], Country: "USA", Credits: "Regia di Frank Darabont · Con Tim Robbins, Morgan Freeman, Bob Gunton, William Sadler")
        { NextChannel = "Iris" };
        string text = NotifyHost.ProposalText(h, Now, TimeZoneInfo.Utc);
        Assert.Equal("★ 9,3 IMDb · 1994 · Dramma, Crimine · USA\nRegia di Frank Darabont · Con Tim Robbins, Morgan Freeman, Bob Gunton\nStasera alle 20:12 su Iris", text);
        var domani = h with { NextAiring = Now.AddDays(1), Rating = null, Credits = "", Country = "" };
        Assert.Equal("1994 · Dramma, Crimine\nDomani alle 15:00 su Iris", NotifyHost.ProposalText(domani, Now, TimeZoneInfo.Utc));
    }
}
