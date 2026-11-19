using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace DoveLoFanno.Updates;

/// <summary>Ultima versione pubblicata, da version.json sul sito (https://dovelofanno.it/version.json).</summary>
public sealed record ReleaseInfo(string Version, string Date, string Notes, string Url);

/// <summary>
/// Controllo degli aggiornamenti (19/11/2026): il server legge una volta al giorno un piccolo file pubblico con l'ultima
/// versione e avvisa se ce n'è una più nuova. Non scarica né installa niente: l'aggiornamento resta a chi amministra
/// (Docker o servizio Windows).
/// </summary>
public static class Releases
{
    /// <summary>Versione di questo programma (dal tag del rilascio; nelle compilazioni di prova X.Y.Z-dev).</summary>
    public static string Current { get; } = Clean(typeof(Releases).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");

    static string Clean(string v) => v.Split('+')[0].Trim();

    /// <summary>Legge version.json; FormatException se manca qualcosa o se l'indirizzo non è https.</summary>
    public static ReleaseInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string Str(string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()!.Trim() : "";
        string version = Clean(Str("version"));
        if (!TryParse(version, out _)) throw new FormatException($"versione non valida: «{version}»");
        string url = Str("url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) throw new FormatException("indirizzo di download mancante o non https");
        string notes = Str("notes");
        if (notes.Length > 500) notes = notes[..500];
        string date = Str("date");
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) date = "";
        return new ReleaseInfo(version, date, notes, url);
    }

    /// <summary>Vero se <paramref name="candidate"/> è una versione più nuova di <paramref name="current"/> (SemVer 2.0).</summary>
    public static bool IsNewer(string candidate, string current) =>
        TryParse(Clean(candidate), out var a) && TryParse(Clean(current), out var b) && Compare(a, b) > 0;

    readonly record struct Ver(int Major, int Minor, int Patch, string[] Pre);

    static bool TryParse(string v, out Ver ver)
    {
        ver = default;
        string core = v, pre = "";
        int dash = v.IndexOf('-');
        if (dash >= 0) { core = v[..dash]; pre = v[(dash + 1)..]; if (pre.Length == 0) return false; }
        var parts = core.Split('.');
        if (parts.Length != 3) return false;
        var n = new int[3];
        for (int i = 0; i < 3; i++)
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit) || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return false;
        var ids = pre.Length == 0 ? [] : pre.Split('.');
        if (ids.Any(x => x.Length == 0 || !x.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))) return false;
        ver = new Ver(n[0], n[1], n[2], ids);
        return true;
    }

    static int Compare(Ver a, Ver b)
    {
        int c = a.Major.CompareTo(b.Major);
        if (c == 0) c = a.Minor.CompareTo(b.Minor);
        if (c == 0) c = a.Patch.CompareTo(b.Patch);
        if (c != 0) return c;
        // a parità di numeri la versione senza trattino (rilascio) è più nuova di quelle con il trattino (0.2.0 > 0.2.0-dev)
        if (a.Pre.Length == 0 && b.Pre.Length == 0) return 0;
        if (a.Pre.Length == 0) return 1;
        if (b.Pre.Length == 0) return -1;
        for (int i = 0; i < Math.Min(a.Pre.Length, b.Pre.Length); i++)
        {
            string x = a.Pre[i], y = b.Pre[i];
            bool xn = x.All(char.IsAsciiDigit), yn = y.All(char.IsAsciiDigit);
            if (xn && yn)
            {
                // numeri di qualsiasi lunghezza: prima il numero di cifre (senza zeri iniziali), poi le cifre
                string xs = x.TrimStart('0'), ys = y.TrimStart('0');
                c = xs.Length != ys.Length ? xs.Length.CompareTo(ys.Length) : string.CompareOrdinal(xs, ys);
            }
            else c = xn ? -1 : yn ? 1 : string.CompareOrdinal(x, y);
            if (c != 0) return Math.Sign(c);
        }
        return a.Pre.Length.CompareTo(b.Pre.Length);
    }
}
