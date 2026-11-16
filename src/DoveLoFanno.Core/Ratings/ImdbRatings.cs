using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace DoveLoFanno.Ratings;

/// <summary>
/// Voti dal file ufficiale IMDb title.ratings.tsv.gz (tconst, averageRating, numVotes), letto in streaming
/// tenendo solo i codici che servono. Licenza IMDb solo personale e non commerciale: nel server è facoltativo.
/// </summary>
public static class ImdbRatings
{
    public const string Url = "https://datasets.imdbws.com/title.ratings.tsv.gz";

    public static Dictionary<string, (double Rating, int Votes)> Read(Stream gzip, IReadOnlySet<string> need)
    {
        var found = new Dictionary<string, (double, int)>();
        using var gz = new GZipStream(gzip, CompressionMode.Decompress, leaveOpen: true);
        using var sr = new StreamReader(gz, Encoding.UTF8);
        while (sr.ReadLine() is { } line)
        {
            int t1 = line.IndexOf('\t');
            if (t1 <= 0 || !need.Contains(line[..t1])) continue;
            string[] f = line.Split('\t');
            if (f.Length >= 3 && double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                && int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                found[f[0]] = (v, n);
        }
        return found;
    }

    public static Dictionary<string, (double Rating, int Votes)> Read(string path, IReadOnlySet<string> need)
    {
        using var fs = File.OpenRead(path);
        return Read(fs, need);
    }
}
