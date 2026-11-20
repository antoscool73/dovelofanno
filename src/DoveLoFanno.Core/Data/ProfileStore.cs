using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

public sealed record ProfileRow(long Id, string Name, bool IsChild, string Avatar, bool HasPin, bool InheritsSubscriptions);

/// <summary>Esito di una modifica ai profili.</summary>
public enum ProfileResult { Ok, NotFound, NameTaken, InvalidName, InvalidPin, LastAdult }

/// <summary>
/// Profili della famiglia. Niente password nella prima versione (la rete di casa è il confine): un PIN facoltativo
/// per gli adulti, salvato solo come impronta PBKDF2. Ogni profilo nasce con la sua lista fissa "La mia serata in TV".
/// Deve restare almeno un profilo adulto.
/// </summary>
public sealed class ProfileStore(string dbPath)
{
    public const string TonightList = "La mia lista";
    static readonly string[] Palette = ["#6E9BFF", "#34C77B", "#F5A25D", "#C79BFF", "#FF7A9C", "#4FC3D9"];

    SqliteConnection Open() => Database.Open(dbPath);

    public List<ProfileRow> List()
    {
        using var c = Open();
        return Db.Query(c, "SELECT id, name, is_child, avatar, pin_hash IS NOT NULL, inherits_subscriptions FROM profiles ORDER BY is_child, id",
            r => new ProfileRow(r.GetInt64(0), r.GetString(1), r.GetInt64(2) == 1, r.GetString(3), r.GetInt64(4) == 1, r.GetInt64(5) == 1));
    }

    public ProfileRow? Get(long id) => List().FirstOrDefault(p => p.Id == id);

    public (ProfileResult Result, long Id) Create(string name, bool isChild, string? avatar, string? pin, DateTimeOffset now)
    {
        name = (name ?? "").Trim();
        if (name.Length is 0 or > 40) return (ProfileResult.InvalidName, 0);
        if (pin != null && (isChild || !ValidPin(pin))) return (ProfileResult.InvalidPin, 0);
        using var c = Open();
        if (NameTaken(c, name, null)) return (ProfileResult.NameTaken, 0);
        using var tx = c.BeginTransaction();
        int count = (int)(Db.ScalarLong(c, "SELECT count(*) FROM profiles") ?? 0);
        string color = string.IsNullOrWhiteSpace(avatar) ? Palette[count % Palette.Length] : avatar.Trim();
        long id = Db.Insert(c, "INSERT INTO profiles (name, is_child, avatar, pin_hash, created_at) VALUES ($n, $c, $a, $p, $t)",
            ("$n", name), ("$c", isChild ? 1 : 0), ("$a", color), ("$p", pin == null ? null : Hash(pin)), ("$t", Db.Unix(now)));
        Db.Exec(c, "INSERT INTO lists (profile_id, name, is_tonight, created_at) VALUES ($p, $n, 1, $t)",
            ("$p", id), ("$n", TonightList), ("$t", Db.Unix(now)));
        tx.Commit();
        return (ProfileResult.Ok, id);
    }

    public ProfileResult Update(long id, string? name, bool? isChild, string? avatar, string? pin, bool clearPin)
    {
        using var c = Open();
        var cur = Db.Query(c, "SELECT is_child, pin_hash IS NOT NULL FROM profiles WHERE id = $id", r => (Child: r.GetInt64(0) == 1, Pin: r.GetInt64(1) == 1), ("$id", id));
        if (cur.Count == 0) return ProfileResult.NotFound;
        bool child = isChild ?? cur[0].Child;
        if (name != null)
        {
            name = name.Trim();
            if (name.Length is 0 or > 40) return ProfileResult.InvalidName;
            if (NameTaken(c, name, id)) return ProfileResult.NameTaken;
        }
        if (pin != null && (child || !ValidPin(pin))) return ProfileResult.InvalidPin;
        if (child && !cur[0].Child && AdultCount(c) <= 1) return ProfileResult.LastAdult;
        using var tx = c.BeginTransaction();
        if (name != null) Db.Exec(c, "UPDATE profiles SET name = $v WHERE id = $id", ("$v", name), ("$id", id));
        if (avatar != null) Db.Exec(c, "UPDATE profiles SET avatar = $v WHERE id = $id", ("$v", avatar.Trim()), ("$id", id));
        if (isChild != null) Db.Exec(c, "UPDATE profiles SET is_child = $v WHERE id = $id", ("$v", child ? 1 : 0), ("$id", id));
        // un profilo bambino non ha PIN
        if (clearPin || child) Db.Exec(c, "UPDATE profiles SET pin_hash = NULL WHERE id = $id", ("$id", id));
        else if (pin != null) Db.Exec(c, "UPDATE profiles SET pin_hash = $v WHERE id = $id", ("$v", Hash(pin)), ("$id", id));
        tx.Commit();
        return ProfileResult.Ok;
    }

    public ProfileResult Delete(long id)
    {
        using var c = Open();
        var child = Db.Query(c, "SELECT is_child FROM profiles WHERE id = $id", r => r.GetInt64(0) == 1, ("$id", id));
        if (child.Count == 0) return ProfileResult.NotFound;
        if (!child[0] && AdultCount(c) <= 1) return ProfileResult.LastAdult;
        Db.Exec(c, "DELETE FROM profiles WHERE id = $id", ("$id", id));
        return ProfileResult.Ok;
    }

    /// <summary>Vero se il profilo non ha PIN o se il PIN è giusto.</summary>
    public bool? Unlock(long id, string? pin)
    {
        using var c = Open();
        var h = Db.Query(c, "SELECT pin_hash FROM profiles WHERE id = $id", r => r.IsDBNull(0) ? "" : r.GetString(0), ("$id", id));
        if (h.Count == 0) return null;
        return h[0].Length == 0 || (pin != null && Verify(pin, h[0]));
    }

    static bool NameTaken(SqliteConnection c, string name, long? except) =>
        (Db.ScalarLong(c, "SELECT count(*) FROM profiles WHERE name = $n COLLATE NOCASE AND ($x IS NULL OR id <> $x)", ("$n", name), ("$x", except)) ?? 0) > 0;

    static int AdultCount(SqliteConnection c) => (int)(Db.ScalarLong(c, "SELECT count(*) FROM profiles WHERE is_child = 0") ?? 0);

    /// <summary>Da 4 a 8 cifre.</summary>
    public static bool ValidPin(string pin) => pin.Length is >= 4 and <= 8 && pin.All(char.IsAsciiDigit);

    // pbkdf2$<iterazioni>$<sale base64>$<impronta base64>
    internal static string Hash(string pin)
    {
        const int iterations = 100_000;
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    internal static bool Verify(string pin, string stored)
    {
        string[] p = stored.Split('$');
        if (p.Length != 4 || p[0] != "pbkdf2" || !int.TryParse(p[1], out int it)) return false;
        byte[] salt = Convert.FromBase64String(p[2]);
        byte[] want = Convert.FromBase64String(p[3]);
        byte[] got = Rfc2898DeriveBytes.Pbkdf2(pin, salt, it, HashAlgorithmName.SHA256, want.Length);
        return CryptographicOperations.FixedTimeEquals(got, want);
    }
}
