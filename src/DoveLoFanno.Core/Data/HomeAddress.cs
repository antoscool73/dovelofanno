using System.Net;
using System.Net.Sockets;

namespace DoveLoFanno.Data;

/// <summary>
/// Indirizzo del server nella rete di casa, per i link dei messaggi Telegram (25/11/2026, scelta di Antonio: solo rete
/// di casa; un indirizzo da fuori casa è tra i «nice to have»). Il server non lo può sapere da sé (in Docker o in WSL vede
/// solo la sua rete interna), quindi lo impara dalle richieste: l'indirizzo che un dispositivo della rete di casa ha
/// scritto nel browser per aprire DoveLoFanno (http://192.168.1.10:8765). Non valgono localhost (dal telefono non porta
/// al server), le reti interne di Docker e WSL (172.16-31) e le richieste da fuori casa o passate da proxy e tunnel.
/// Un indirizzo numerico vince su un nome, che il telefono potrebbe non conoscere.
/// </summary>
public static class HomeAddress
{
    public const string Key = "HomeUrl";
    static readonly object Gate = new();
    static string? last;     // ultimo valore visto, per non scrivere nel database a ogni richiesta

    /// <summary>L'indirizzo imparato (http://192.168.1.10:8765), vuoto se nessun dispositivo di casa l'ha ancora usato.</summary>
    public static string Get(SettingsStore s) => s.Get(Key);

    /// <summary>
    /// Da chiamare per ogni richiesta: <paramref name="fromHome"/> vero se viene dalla rete di casa senza proxy.
    /// Restituisce vero se l'indirizzo salvato è cambiato.
    /// </summary>
    public static bool Remember(SettingsStore s, bool fromHome, IPAddress? client, string scheme, string host)
    {
        if (!fromHome || client == null || host.Length == 0) return false;
        if (client.IsIPv4MappedToIPv6) client = client.MapToIPv4();
        if (IPAddress.IsLoopback(client)) return false;                 // il browser sullo stesso computer del server
        if (Candidate(scheme, host) is not { } url) return false;
        lock (Gate)
        {
            if (url == last) return false;
            string saved = s.Get(Key);
            if (url == saved) { last = url; return false; }
            if (!IsNumeric(url) && saved.Length > 0 && IsNumeric(saved)) return false;   // un nome non sostituisce un indirizzo numerico
            s.Set(Key, url);
            last = url;
            return true;
        }
    }

    /// <summary>schema://host[:porta] se l'host è utile da un altro dispositivo di casa, altrimenti null.</summary>
    public static string? Candidate(string scheme, string host)
    {
        if (scheme is not ("http" or "https")) return null;
        if (!Uri.TryCreate($"{scheme}://{host}/", UriKind.Absolute, out var u) || u.Host.Length == 0) return null;
        string h = u.Host.Trim('[', ']');
        if (h.Equals("localhost", StringComparison.OrdinalIgnoreCase) || h.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return null;
        if (IPAddress.TryParse(h, out var ip))
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (IPAddress.IsLoopback(ip) || !AuthStore.IsHomeNetwork(ip)) return null;
            if (ip.AddressFamily == AddressFamily.InterNetwork && ip.GetAddressBytes() is [172, >= 16 and <= 31, ..]) return null;
        }
        return u.GetLeftPart(UriPartial.Authority);
    }

    static bool IsNumeric(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && IPAddress.TryParse(u.Host.Trim('[', ']'), out _);

    /// <summary>Solo per i test: dimentica il valore tenuto in memoria.</summary>
    public static void ResetCache() { lock (Gate) last = null; }
}
