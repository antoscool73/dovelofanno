using DoveLoFanno.Data;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Tests;

public sealed class DatabaseTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "dlf-db-" + Guid.NewGuid().ToString("N"));
    string DbPath => Path.Combine(dir, "dlf.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    static long Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static readonly string[] Tables =
    [
        "profiles", "subscriptions", "settings", "sources", "guide_sources", "guide_channels", "channels", "streams",
        "works", "programmes", "rating_cache", "recordings", "recording_parts", "library_items", "watch_states",
        "lists", "list_items", "preferences", "ignored_works", "proposals_seen", "availability", "alerts", "alert_events",
        "library_folders", "notifications", "list_home", "series_rules", "conversions",
    ];

    [Fact]
    public void MigrazioniIncorporateEConsecutive()
    {
        Assert.NotEmpty(Database.Migrations);
        Assert.Equal(1, Database.Migrations[0].Version);
        Assert.Equal(Database.Migrations.Count, Database.LatestVersion);
    }

    [Fact]
    public void CreaDaZeroTutteLeTabelle()
    {
        using var c = Database.Open(DbPath);
        Assert.Equal(Database.LatestVersion, Database.UserVersion(c));
        foreach (var t in Tables)
            Assert.Equal(1, Scalar(c, $"SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = '{t}'"));
        Assert.Equal(Tables.Length, Scalar(c, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'"));
        Assert.Equal(1, Scalar(c, "PRAGMA foreign_keys"));
        Assert.Equal("wal", (string?)new SqliteCommand("PRAGMA journal_mode", c).ExecuteScalar());
        Assert.Equal(0, Scalar(c, "SELECT count(*) FROM pragma_foreign_key_check"));
        Assert.Empty(Directory.GetFiles(dir, "*.bak"));                      // database nuovo: niente copia
    }

    [Fact]
    public void RiaperturaNonCambiaNulla()
    {
        using (var c = Database.Open(DbPath))
            Exec(c, "INSERT INTO settings (key, value) VALUES ('MaxParallel', '3')");
        using (var c = Database.Open(DbPath))
        {
            Assert.Equal(Database.LatestVersion, Database.UserVersion(c));
            Assert.Equal(1, Scalar(c, "SELECT count(*) FROM settings"));
        }
        Assert.Empty(Directory.GetFiles(dir, "*.bak"));
    }

    [Fact]
    public void DatabasePiuNuovoNonSiTocca()
    {
        using (var c = Database.Open(DbPath))
            Exec(c, $"PRAGMA user_version = {Database.LatestVersion + 1}");
        var ex = Assert.Throws<DatabaseTooNewException>(() => Database.Open(DbPath));
        Assert.Contains("aggiornare il programma", ex.Message);
    }

    static Migration Extra(int version, string sql) => new(version, "prova", sql);

    [Fact]
    public void AggiornamentoConCopiaDiSicurezzaEDatiConservati()
    {
        using (var c = Database.Open(DbPath))
            Exec(c, "INSERT INTO profiles (name, created_at) VALUES ('Anna', 0)");
        int v = Database.LatestVersion;
        var more = Database.Migrations.Append(Extra(v + 1, "ALTER TABLE profiles ADD COLUMN color TEXT NOT NULL DEFAULT '';")).ToList();
        using (var c = Database.Open(DbPath, more))
        {
            Assert.Equal(v + 1, Database.UserVersion(c));
            Assert.Equal(1, Scalar(c, "SELECT count(*) FROM profiles WHERE name = 'Anna' AND color = ''"));
        }
        string bak = DbPath + ".v" + v + ".bak";
        Assert.True(File.Exists(bak));
        using var old = new SqliteConnection($"Data Source={bak};Pooling=False");
        old.Open();
        Assert.Equal(v, Database.UserVersion(old));
        Assert.Equal(1, Scalar(old, "SELECT count(*) FROM profiles"));
    }

    [Fact]
    public void MigrazioneFallitaVieneAnnullata()
    {
        using (var c = Database.Open(DbPath))
            Exec(c, "INSERT INTO profiles (name, created_at) VALUES ('Anna', 0)");
        int v = Database.LatestVersion;
        var bad = Database.Migrations.Append(Extra(v + 1, "ALTER TABLE profiles ADD COLUMN color TEXT; SELECT * FROM tabella_inesistente;")).ToList();
        Assert.Throws<SqliteException>(() => Database.Open(DbPath, bad));
        using var c2 = Database.Open(DbPath);
        Assert.Equal(v, Database.UserVersion(c2));
        Assert.Equal(0, Scalar(c2, "SELECT count(*) FROM pragma_table_info('profiles') WHERE name = 'color'"));
    }

    [Fact]
    public void MigrazioneCheRompeLeChiaviEsterneVieneAnnullata()
    {
        Database.Open(DbPath).Dispose();
        int v = Database.LatestVersion;
        var bad = Database.Migrations.Append(Extra(v + 1, "INSERT INTO subscriptions (profile_id, service) VALUES (999, 'netflix');")).ToList();
        var ex = Assert.Throws<InvalidOperationException>(() => Database.Open(DbPath, bad));
        Assert.Contains("subscriptions", ex.Message);
        using var c = Database.Open(DbPath);
        Assert.Equal(0, Scalar(c, "SELECT count(*) FROM subscriptions"));
    }

    [Fact]
    public void Vincoli()
    {
        using var c = Database.Open(DbPath);
        Exec(c, "INSERT INTO profiles (id, name, created_at) VALUES (1, 'Anna', 0), (2, 'Bimbo', 0)");

        // chiavi esterne attive
        Assert.Throws<SqliteException>(() => Exec(c, "INSERT INTO lists (profile_id, name, created_at) VALUES (99, 'x', 0)"));
        // una sola "La mia serata in TV" per profilo, e mai della famiglia
        Exec(c, "INSERT INTO lists (profile_id, name, is_tonight, created_at) VALUES (1, 'La mia serata in TV', 1, 0)");
        Assert.Throws<SqliteException>(() => Exec(c, "INSERT INTO lists (profile_id, name, is_tonight, created_at) VALUES (1, 'Doppia', 1, 0)"));
        Assert.Throws<SqliteException>(() => Exec(c, "INSERT INTO lists (profile_id, name, is_tonight, created_at) VALUES (NULL, 'Famiglia', 1, 0)"));
        // abbonamenti della famiglia senza doppioni (i NULL di UNIQUE non bastano)
        Exec(c, "INSERT INTO subscriptions (profile_id, service) VALUES (NULL, 'netflix'), (1, 'netflix')");
        Assert.Throws<SqliteException>(() => Exec(c, "INSERT INTO subscriptions (profile_id, service) VALUES (NULL, 'netflix')"));
        // voto personale da 1 a 5
        Exec(c, "INSERT INTO works (id, kind, work_key, title, title_key, updated_at) VALUES (1, 'film', 'padrino|1972', 'Il Padrino', 'padrino', 0)");
        Assert.Throws<SqliteException>(() => Exec(c, "INSERT INTO watch_states (profile_id, work_id, stars, updated_at) VALUES (1, 1, 6, 0)"));
        Exec(c, "INSERT INTO watch_states (profile_id, work_id, stars, updated_at) VALUES (1, 1, 5, 0)");
        Assert.Throws<SqliteException>(() => Exec(c, "INSERT INTO watch_states (profile_id, work_id, updated_at) VALUES (1, 1, 0)"));
        // gruppi della guida solo quelli di Genres
        Exec(c, "INSERT INTO guide_sources (id, url) VALUES (1, 'https://example.org/guida.xml.gz')");
        Exec(c, "INSERT INTO guide_channels (id, guide_source_id, xmltv_id) VALUES (1, 1, 'Canale.5.it')");
        Assert.Throws<SqliteException>(() => Exec(c, "INSERT INTO programmes (guide_channel_id, start, stop, title, group_name) VALUES (1, 0, 10, 'x', 'Fumetti')"));
        foreach (var g in DoveLoFanno.Epg.Genres.Groups)
            Exec(c, $"INSERT INTO programmes (guide_channel_id, start, stop, title, group_name) VALUES (1, 0, 10, '{g}', '{g}')");
        // cancellando un profilo spariscono le sue cose
        Exec(c, "DELETE FROM profiles WHERE id = 1");
        Assert.Equal(0, Scalar(c, "SELECT count(*) FROM watch_states"));
        Assert.Equal(0, Scalar(c, "SELECT count(*) FROM lists"));
        Assert.Equal(1, Scalar(c, "SELECT count(*) FROM subscriptions"));
    }
}
