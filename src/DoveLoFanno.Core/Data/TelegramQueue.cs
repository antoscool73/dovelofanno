using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DoveLoFanno.Data;

/// <summary>
/// Un messaggio Telegram rimasto da mandare: il testo e quando è nata la notifica. Dal 25/11/2026 anche la locandina
/// dell'opera (indirizzo TMDB, vuoto = solo testo) e il link alla scheda nella rete di casa (vuoto = nessun link).
/// </summary>
public sealed record QueuedMessage(DateTimeOffset At, string Text, string Photo = "", string Link = "");

/// <summary>
/// Coda dei messaggi Telegram (24/11/2026, richiesta di Antonio): le notifiche nate negli orari di silenzio, o quando
/// Telegram non risponde, non si perdono: restano nell'impostazione TelegramQueue (sopravvive ai riavvii) e partono tutte
/// insieme, in un messaggio solo, appena il silenzio finisce o Telegram torna raggiungibile.
/// </summary>
public static class TelegramQueue
{
    const string Key = "TelegramQueue";
    /// <summary>Oltre questo numero si tengono solo le più recenti: le altre restano nella campanella.</summary>
    public const int MaxItems = 200;
    /// <summary>Righe in un messaggio (Telegram accetta al massimo 4096 caratteri).</summary>
    public const int MaxLines = 30;

    static readonly object Gate = new();
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    static List<QueuedMessage> Read(SettingsStore s)
    {
        string v = s.Get(Key);
        if (v.Length == 0) return [];
        try { return JsonSerializer.Deserialize<List<QueuedMessage>>(v, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    static void Write(SettingsStore s, List<QueuedMessage> items) =>
        s.Set(Key, items.Count == 0 ? "" : JsonSerializer.Serialize(items, Json));

    public static int Count(SettingsStore s) { lock (Gate) return Read(s).Count; }

    public static void Add(SettingsStore s, IEnumerable<QueuedMessage> add)
    {
        lock (Gate)
        {
            var items = Read(s);
            items.AddRange(add);
            items = items.OrderBy(m => m.At).ToList();
            if (items.Count > MaxItems) items = items.Skip(items.Count - MaxItems).ToList();
            Write(s, items);
        }
    }

    /// <summary>Toglie dalla coda tutti i messaggi e li restituisce (in ordine di tempo); chi non riesce a mandarli li rimette con Add.</summary>
    public static List<QueuedMessage> TakeAll(SettingsStore s)
    {
        lock (Gate)
        {
            var items = Read(s);
            if (items.Count > 0) Write(s, []);
            return items;
        }
    }

    public static void Clear(SettingsStore s) { lock (Gate) Write(s, []); }

    /// <summary>
    /// Il messaggio unico per la coda: un messaggio solo resta com'è (locandina e pulsante li aggiunge chi lo manda), più
    /// messaggi diventano un elenco con l'ora e il link sotto ciascuno, senza locandine (non dieci foto di fila al risveglio).
    /// </summary>
    public static string Format(IReadOnlyList<QueuedMessage> items, TimeZoneInfo zone)
    {
        if (items.Count == 1) return items[0].Text;
        var sb = new StringBuilder($"{items.Count} notifiche arrivate mentre i messaggi erano in pausa:");
        var today = TimeZoneInfo.ConvertTime(items[^1].At, zone).Date;
        foreach (var m in items.Take(MaxLines))
        {
            var t = TimeZoneInfo.ConvertTime(m.At, zone);
            string when = t.Date == today ? t.ToString("HH:mm", CultureInfo.InvariantCulture) : t.ToString("ddd d HH:mm", CultureInfo.GetCultureInfo("it-IT"));
            sb.Append("\n\n").Append(when).Append(" · ").Append(m.Text);
            if (m.Link.Length > 0) sb.Append('\n').Append(m.Link);
        }
        if (items.Count > MaxLines) sb.Append($"\n\n… e altre {items.Count - MaxLines}: sono nella campanella di DoveLoFanno.");
        string text = sb.ToString();
        return text.Length <= 4000 ? text : text[..3990] + "…";
    }
}
