using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

/// <summary>Una migrazione: numero di versione a cui porta il database e SQL da eseguire.</summary>
public sealed record Migration(int Version, string Name, string Sql);

/// <summary>Il database è di una versione più nuova di quella che questo programma conosce.</summary>
public sealed class DatabaseTooNewException(int found, int known)
    : Exception($"il database è alla versione {found}, questo programma conosce fino alla {known}: aggiornare il programma") { }

/// <summary>
/// Database SQLite di DoveLoFanno: un file nella cartella dati. All'apertura applica le migrazioni mancanti
/// (file Data/Migrations/NNNN_nome.sql incorporati nella libreria), ciascuna in una transazione, con
/// PRAGMA user_version come versione. Prima di aggiornare un database esistente ne fa una copia accanto
/// (dlf.db.v1.bak); un database più nuovo del programma non viene toccato.
/// </summary>
public static partial class Database
{
    public static IReadOnlyList<Migration> Migrations { get; } = LoadEmbedded();

    public static int LatestVersion => Migrations.Count == 0 ? 0 : Migrations[^1].Version;

    /// <summary>Apre (o crea) il database, lo porta all'ultima versione e restituisce la connessione aperta.</summary>
    public static SqliteConnection Open(string path) => Open(path, Migrations);

    internal static SqliteConnection Open(string path, IReadOnlyList<Migration> migrations)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString();
        var conn = new SqliteConnection(cs);
        conn.Open();
        try
        {
            Exec(conn, "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;");
            Migrate(conn, path, migrations);
            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    public static int UserVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    static void Migrate(SqliteConnection conn, string path, IReadOnlyList<Migration> migrations)
    {
        int current = UserVersion(conn);
        int latest = migrations.Count == 0 ? 0 : migrations[^1].Version;
        if (current > latest) throw new DatabaseTooNewException(current, latest);
        if (current == latest) return;
        if (current > 0) Backup(conn, path + ".v" + current + ".bak");

        // le chiavi esterne si controllano alla fine di ogni migrazione, non istruzione per istruzione
        Exec(conn, "PRAGMA foreign_keys = OFF;");
        try
        {
            foreach (var m in migrations.Where(m => m.Version > current))
            {
                using var tx = conn.BeginTransaction();
                Exec(conn, m.Sql, tx);
                using (var check = conn.CreateCommand())
                {
                    check.Transaction = tx;
                    check.CommandText = "PRAGMA foreign_key_check;";
                    using var r = check.ExecuteReader();
                    if (r.Read()) throw new InvalidOperationException($"migrazione {m.Version} ({m.Name}): chiavi esterne non valide nella tabella {r.GetString(0)}");
                }
                Exec(conn, $"PRAGMA user_version = {m.Version};", tx);
                tx.Commit();
            }
        }
        finally
        {
            Exec(conn, "PRAGMA foreign_keys = ON;");
        }
    }

    static void Backup(SqliteConnection conn, string target)
    {
        if (File.Exists(target)) File.Delete(target);
        using var dest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString());
        dest.Open();
        conn.BackupDatabase(dest);
    }

    static void Exec(SqliteConnection conn, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static List<Migration> LoadEmbedded()
    {
        var asm = typeof(Database).Assembly;
        var list = new List<Migration>();
        foreach (string res in asm.GetManifestResourceNames())
        {
            var m = MigrationName().Match(res);
            if (!m.Success) continue;
            using var s = asm.GetManifestResourceStream(res)!;
            using var sr = new StreamReader(s);
            list.Add(new Migration(int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), m.Groups[2].Value, sr.ReadToEnd()));
        }
        list.Sort((a, b) => a.Version.CompareTo(b.Version));
        for (int i = 0; i < list.Count; i++)
            if (list[i].Version != i + 1) throw new InvalidOperationException($"migrazioni non consecutive: manca la {i + 1}");
        return list;
    }

    [GeneratedRegex(@"\.Data\.Migrations\.(\d{4})_(\w+)\.sql$")]
    private static partial Regex MigrationName();
}
