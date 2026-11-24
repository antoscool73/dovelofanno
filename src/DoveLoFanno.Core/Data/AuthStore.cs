using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DoveLoFanno.Data;

/// <summary>Un dispositivo che ha fatto l'accesso: si conserva solo l'impronta del suo gettone.</summary>
public sealed record AuthSession(string Hash, DateTimeOffset Created, DateTimeOffset LastSeen, string Device);

/// <summary>
/// Password del server (24/11/2026, prima della pubblicazione): una sola per la famiglia, come in Jellyfin. Senza password
/// il server risponde solo alla rete di casa; con la password ogni dispositivo la inserisce una volta e riceve un gettone
/// (cookie) che vale finché non si esce o non si cambia la password. Tutto sta nelle impostazioni: ServerPassword
/// (PBKDF2-SHA256 con sale) e AuthSessions (impronte SHA-256 dei gettoni, mai i gettoni).
/// </summary>
public sealed class AuthStore(SettingsStore settings)
{
    const string PasswordKey = "ServerPassword", SessionsKey = "AuthSessions";
    const int Iterations = 210_000;
    public const int MinLength = 8;
    /// <summary>Un gettone non usato per tanto tempo scade da solo.</summary>
    public static readonly TimeSpan IdleLimit = TimeSpan.FromDays(180);
    static readonly object Gate = new();
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool HasPassword => settings.Get(PasswordKey).Length > 0;

    public static string Hash(string password, byte[]? salt = null)
    {
        salt ??= RandomNumberGenerator.GetBytes(16);
        byte[] h = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(h)}";
    }

    public bool Check(string? password)
    {
        string stored = settings.Get(PasswordKey);
        if (stored.Length == 0 || string.IsNullOrEmpty(password)) return false;
        var p = stored.Split('$');
        if (p.Length != 4 || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int iter)) return false;
        byte[] salt = Convert.FromBase64String(p[2]), want = Convert.FromBase64String(p[3]);
        byte[] got = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iter, HashAlgorithmName.SHA256, want.Length);
        return CryptographicOperations.FixedTimeEquals(got, want);
    }

    /// <summary>Messaggio d'errore in italiano, o vuoto se la password va bene.</summary>
    public static string Validate(string? password) =>
        password == null || password.Trim().Length < MinLength ? $"la password deve avere almeno {MinLength} caratteri" : "";

    /// <summary>Imposta o cambia la password: tutti i dispositivi devono rifare l'accesso.</summary>
    public void SetPassword(string password)
    {
        lock (Gate) { settings.Set(PasswordKey, Hash(password)); settings.Set(SessionsKey, ""); }
    }

    /// <summary>Toglie la password: il server torna a rispondere solo alla rete di casa.</summary>
    public void ClearPassword()
    {
        lock (Gate) { settings.Set(PasswordKey, ""); settings.Set(SessionsKey, ""); }
    }

    static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    List<AuthSession> Read()
    {
        string v = settings.Get(SessionsKey);
        if (v.Length == 0) return [];
        try { return JsonSerializer.Deserialize<List<AuthSession>>(v, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    void Write(List<AuthSession> s) => settings.Set(SessionsKey, s.Count == 0 ? "" : JsonSerializer.Serialize(s, Json));

    /// <summary>Nuovo gettone per un dispositivo (restituito una volta sola: si salva solo la sua impronta).</summary>
    public string CreateSession(string device, DateTimeOffset now)
    {
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        lock (Gate)
        {
            var s = Read().Where(x => now - x.LastSeen < IdleLimit).ToList();
            s.Add(new AuthSession(TokenHash(token), now, now, device.Length > 120 ? device[..120] : device));
            if (s.Count > 50) s = s.OrderByDescending(x => x.LastSeen).Take(50).ToList();
            Write(s);
        }
        return token;
    }

    /// <summary>Vero se il gettone è valido; ne aggiorna l'ultimo uso (al massimo una volta all'ora).</summary>
    public bool IsValid(string? token, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(token)) return false;
        string h = TokenHash(token);
        lock (Gate)
        {
            var s = Read();
            int i = s.FindIndex(x => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(x.Hash), Encoding.ASCII.GetBytes(h)));
            if (i < 0 || now - s[i].LastSeen >= IdleLimit) return false;
            if (now - s[i].LastSeen > TimeSpan.FromHours(1)) { s[i] = s[i] with { LastSeen = now }; Write(s); }
            return true;
        }
    }

    public void DeleteSession(string? token)
    {
        if (string.IsNullOrEmpty(token)) return;
        string h = TokenHash(token);
        lock (Gate) Write(Read().Where(x => x.Hash != h).ToList());
    }

    public int SessionCount { get { lock (Gate) return Read().Count; } }

    /// <summary>
    /// Indirizzo della rete di casa: loopback, reti private (10/8, 172.16/12, 192.168/16), link-local, IPv6 locali (fc00::/7,
    /// fe80::/10). Senza indirizzo (test, socket locali) vale come casa.
    /// </summary>
    public static bool IsHomeNetwork(IPAddress? ip)
    {
        if (ip == null) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] b = ip.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC || ip.IsIPv6LinkLocal;
        }
        return false;
    }
}
