using DoveLoFanno.Data;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

/// <summary>Coda dei messaggi Telegram per gli orari di silenzio (24/11/2026).</summary>
public sealed class TelegramQueueTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-tg-" + Guid.NewGuid().ToString("N"));
    static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Rome");

    public TelegramQueueTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    SettingsStore Settings() => new(Path.Combine(dir, "dlf.db"));

    [Fact]
    public void LaCodaSopravviveERestaInOrdine()
    {
        var s = Settings();
        var t = new DateTimeOffset(2026, 11, 24, 23, 0, 0, TimeSpan.Zero);
        TelegramQueue.Add(s, [new QueuedMessage(t.AddMinutes(30), "seconda")]);
        TelegramQueue.Add(Settings(), [new QueuedMessage(t, "prima")]);      // un'altra istanza: la coda è nel database
        Assert.Equal(2, TelegramQueue.Count(s));

        var items = TelegramQueue.TakeAll(s);
        Assert.Equal(["prima", "seconda"], items.Select(m => m.Text));
        Assert.Equal(0, TelegramQueue.Count(s));
        Assert.Empty(TelegramQueue.TakeAll(s));
    }

    [Fact]
    public void TieneSoloLePiuRecenti()
    {
        var s = Settings();
        var t = new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);
        TelegramQueue.Add(s, Enumerable.Range(0, TelegramQueue.MaxItems + 5).Select(i => new QueuedMessage(t.AddMinutes(i), "n" + i)));
        var items = TelegramQueue.TakeAll(s);
        Assert.Equal(TelegramQueue.MaxItems, items.Count);
        Assert.Equal("n5", items[0].Text);
    }

    [Fact]
    public void UnMessaggioSoloRestaUguale()
    {
        var m = new QueuedMessage(new DateTimeOffset(2026, 11, 24, 22, 15, 0, TimeSpan.Zero), "Registrata: «X»\nRai 1");
        Assert.Equal("Registrata: «X»\nRai 1", TelegramQueue.Format([m], Rome));
    }

    [Fact]
    public void PiuMessaggiDiventanoUnElencoConLOra()
    {
        var t = new DateTimeOffset(2026, 11, 24, 22, 15, 0, TimeSpan.Zero);     // 23:15 a Roma
        string text = TelegramQueue.Format([new QueuedMessage(t, "Registrata: «X»"), new QueuedMessage(t.AddHours(2), "Avviso: «Y»")], Rome);
        Assert.StartsWith("2 notifiche arrivate mentre i messaggi erano in pausa:", text);
        Assert.Contains("mar 24 23:15 · Registrata: «X»", text);      // giorno prima dell'ultima: con la data
        Assert.Contains("01:15 · Avviso: «Y»", text);

        var many = Enumerable.Range(0, TelegramQueue.MaxLines + 3).Select(i => new QueuedMessage(t.AddMinutes(i), "n" + i)).ToList();
        string longText = TelegramQueue.Format(many, Rome);
        Assert.Contains("e altre 3", longText);
        Assert.True(longText.Length <= 4000);
    }
}
