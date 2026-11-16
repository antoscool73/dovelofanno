using System.Globalization;

namespace DoveLoFanno.Data;

/// <summary>Impostazioni generali (tabella settings), con i valori predefiniti di RegistraTV.</summary>
public sealed class SettingsStore(string dbPath)
{
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["BeforeMin"] = "2",
        ["AfterMin"] = "10",
        ["Format"] = "mp4",
        ["MaxParallel"] = "3",
        ["MinFreeGB"] = "20",
    };

    public string Get(string key)
    {
        using var c = Database.Open(dbPath);
        var v = Db.Query(c, "SELECT value FROM settings WHERE key = $k", r => r.GetString(0), ("$k", key)).FirstOrDefault();
        return v ?? Defaults.GetValueOrDefault(key, "");
    }

    public int GetInt(string key) => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    public void Set(string key, string value)
    {
        using var c = Database.Open(dbPath);
        Db.Exec(c, "INSERT INTO settings (key, value) VALUES ($k, $v) ON CONFLICT (key) DO UPDATE SET value = $v", ("$k", key), ("$v", value));
    }

    public Dictionary<string, string> All()
    {
        using var c = Database.Open(dbPath);
        var res = new Dictionary<string, string>(Defaults);
        foreach (var (k, v) in Db.Query(c, "SELECT key, value FROM settings", r => (r.GetString(0), r.GetString(1)))) res[k] = v;
        return res;
    }
}
