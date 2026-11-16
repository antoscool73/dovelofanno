using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

public sealed record BackupInfo(string Name, long Bytes, DateTimeOffset CreatedAt);

/// <summary>
/// Copie del database nella cartella dati (backup/dlf-AAAAMMGG-HHMMSS.db), fatte con l'API di backup di SQLite:
/// sono coerenti anche mentre il server scrive. Si tengono le ultime <see cref="Keep"/>. Le registrazioni non si
/// copiano (sono grandi e stanno nella loro cartella); per ripristinare: fermare il server e rimettere la copia
/// al posto di dlf.db.
/// </summary>
public sealed class Backup(string dbPath, string backupDir)
{
    public int Keep { get; init; } = 7;

    public BackupInfo Create(DateTimeOffset now)
    {
        Directory.CreateDirectory(backupDir);
        string name = "dlf-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".db";
        string target = Path.Combine(backupDir, name);
        string tmp = target + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (var src = Database.Open(dbPath))
        using (var dest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tmp, Pooling = false }.ToString()))
        {
            dest.Open();
            src.BackupDatabase(dest);
        }
        File.Move(tmp, target, overwrite: true);
        Prune();
        return Info(new FileInfo(target));
    }

    public List<BackupInfo> List() =>
        Directory.Exists(backupDir)
            ? new DirectoryInfo(backupDir).GetFiles("dlf-*.db").OrderByDescending(f => f.Name, StringComparer.Ordinal).Select(Info).ToList()
            : [];

    void Prune()
    {
        foreach (var f in new DirectoryInfo(backupDir).GetFiles("dlf-*.db").OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(Keep))
            f.Delete();
    }

    static BackupInfo Info(FileInfo f) => new(f.Name, f.Length, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero));
}
