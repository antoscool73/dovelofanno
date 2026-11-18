using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

/// <summary>Abbonamenti di un profilo: se eredita quelli della famiglia, quelli in uso e quelli suoi.</summary>
public sealed record ProfileSubscriptions(bool Inherits, IReadOnlyList<string> Services, IReadOnlyList<string> Own);

/// <summary>
/// Abbonamenti della famiglia e dei profili (piattaforme di streaming, con i nomi di JustWatch/TMDB). Ogni profilo eredita
/// quelli della famiglia finché non sceglie i suoi; la sua scelta parte da quella della famiglia.
/// </summary>
public sealed class SubscriptionStore(string dbPath)
{
    /// <summary>Le piattaforme più comuni in Italia, con i nomi che usa TMDB, per scegliere anche prima di averle viste in una scheda.</summary>
    public static readonly string[] Common =
    [
        "Netflix", "Amazon Prime Video", "Disney Plus", "Now TV", "Sky Go", "Apple TV", "Paramount Plus", "Rai Play",
        "Mediaset Infinity", "Timvision", "Discovery+", "DAZN", "HBO Max", "MUBI", "Crunchyroll", "Rakuten TV", "Pluto TV",
    ];

    SqliteConnection Open() => Database.Open(dbPath);

    public List<string> Family()
    {
        using var c = Open();
        return Db.Query(c, "SELECT service FROM subscriptions WHERE profile_id IS NULL ORDER BY service COLLATE NOCASE", r => r.GetString(0));
    }

    public void SetFamily(IEnumerable<string> services)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Db.Exec(c, "DELETE FROM subscriptions WHERE profile_id IS NULL");
        foreach (string s in Clean(services))
            Db.Exec(c, "INSERT INTO subscriptions (profile_id, service) VALUES (NULL, $s)", ("$s", s));
        tx.Commit();
    }

    /// <summary>null se il profilo non esiste.</summary>
    public ProfileSubscriptions? OfProfile(long profileId)
    {
        using var c = Open();
        var inh = Db.Query(c, "SELECT inherits_subscriptions FROM profiles WHERE id = $p", r => r.GetInt64(0) == 1, ("$p", profileId));
        if (inh.Count == 0) return null;
        var own = Db.Query(c, "SELECT service FROM subscriptions WHERE profile_id = $p ORDER BY service COLLATE NOCASE", r => r.GetString(0), ("$p", profileId));
        c.Dispose();
        return new ProfileSubscriptions(inh[0], inh[0] ? Family() : own, own);
    }

    /// <summary>Eredita dalla famiglia, oppure tiene una scelta sua. false se il profilo non esiste.</summary>
    public bool SetProfile(long profileId, bool inherits, IEnumerable<string>? services)
    {
        using var c = Open();
        if (Db.ScalarLong(c, "SELECT id FROM profiles WHERE id = $p", ("$p", profileId)) == null) return false;
        using var tx = c.BeginTransaction();
        Db.Exec(c, "UPDATE profiles SET inherits_subscriptions = $i WHERE id = $p", ("$i", inherits ? 1 : 0), ("$p", profileId));
        if (services != null)
        {
            Db.Exec(c, "DELETE FROM subscriptions WHERE profile_id = $p", ("$p", profileId));
            foreach (string s in Clean(services))
                Db.Exec(c, "INSERT INTO subscriptions (profile_id, service) VALUES ($p, $s)", ("$p", profileId), ("$s", s));
        }
        tx.Commit();
        return true;
    }

    /// <summary>Piattaforme da proporre: le comuni più quelle già viste nelle schede (abbonamento o gratis).</summary>
    public List<string> Known()
    {
        using var c = Open();
        // "Amazon Prime Video with Ads" non si sceglie a parte: chi ha Prime Video lo vede già (vedi streamingFor nel client)
        var seen = Db.Query(c, "SELECT DISTINCT platform FROM availability WHERE kind IN ('subscription', 'free')", r => r.GetString(0))
            .Where(s => !s.EndsWith(" with Ads", StringComparison.OrdinalIgnoreCase)
                        && !s.StartsWith("JustWatch ", StringComparison.OrdinalIgnoreCase))   // il canale di JustWatch stesso, non un abbonamento
            .ToList();
        seen.AddRange(Family());
        return Common.Concat(seen).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static IEnumerable<string> Clean(IEnumerable<string> services) =>
        services.Select(s => (s ?? "").Trim()).Where(s => s.Length is > 0 and <= 60).Distinct(StringComparer.OrdinalIgnoreCase);
}
