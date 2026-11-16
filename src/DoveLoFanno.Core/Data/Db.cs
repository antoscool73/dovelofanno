using Microsoft.Data.Sqlite;

namespace DoveLoFanno.Data;

/// <summary>Piccoli aiuti per l'SQL scritto a mano: parametri, letture, orari in secondi Unix UTC.</summary>
internal static class Db
{
    public static long Unix(DateTimeOffset t) => t.ToUnixTimeSeconds();
    public static DateTimeOffset FromUnix(long s) => DateTimeOffset.FromUnixTimeSeconds(s);

    public static DateTimeOffset? NTime(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : FromUnix(r.GetInt64(i));
    public static int? NInt(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
    public static long? NLong(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    static SqliteCommand Cmd(SqliteConnection c, string sql, (string Name, object? Value)[] ps)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }

    public static int Exec(SqliteConnection c, string sql, params (string, object?)[] ps)
    {
        using var cmd = Cmd(c, sql, ps);
        return cmd.ExecuteNonQuery();
    }

    public static long Insert(SqliteConnection c, string sql, params (string, object?)[] ps)
    {
        using var cmd = Cmd(c, sql + " RETURNING id", ps);
        return Convert.ToInt64(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static long? ScalarLong(SqliteConnection c, string sql, params (string, object?)[] ps)
    {
        using var cmd = Cmd(c, sql, ps);
        object? v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static List<T> Query<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> map, params (string, object?)[] ps)
    {
        using var cmd = Cmd(c, sql, ps);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }
}
