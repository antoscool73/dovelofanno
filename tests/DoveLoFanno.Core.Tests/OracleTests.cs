using System.Globalization;
using DoveLoFanno.Epg;
using Xunit.Abstractions;

namespace DoveLoFanno.Tests;

/// <summary>
/// Confronto con la versione PowerShell (l'"oracolo" del piano): stessa guida salvata, stessi programmi,
/// stessi generi e WorkKey. I dati li genera tools/oracle/Export-OracleGuide.ps1 in tools/oracle/data,
/// che resta fuori da git: senza dati il test non controlla nulla e lo dice.
/// </summary>
public class OracleTests(ITestOutputHelper output)
{
    static string? DataDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "tools", "oracle", "data");
            if (File.Exists(Path.Combine(p, "guide-oracle.tsv"))) return p;
        }
        return null;
    }

    static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    [Fact]
    public void StessaGuidaStessiRisultati()
    {
        string? dir = DataDir();
        if (dir == null)
        {
            output.WriteLine("Dati dell'oracolo assenti: lanciare tools/oracle/Export-OracleGuide.ps1");
            return;
        }
        var inv = CultureInfo.InvariantCulture;
        var lines = File.ReadAllLines(Path.Combine(dir, "guide-oracle.tsv"));
        var now = DateTime.ParseExact(lines[0].Split('\t')[1], "yyyy-MM-ddTHH:mm:ss", inv);
        var expected = lines.Skip(1).ToList();

        var g = XmltvReader.Load(Path.Combine(dir, "guide.xml.gz"), now);
        var actual = new List<string>();
        foreach (string id in g.ChannelIds.Order(StringComparer.OrdinalIgnoreCase))
            foreach (var p in g.Programmes(id))
                actual.Add(string.Join('\t',
                    p.Channel, p.Start.ToString("yyyy-MM-ddTHH:mm:ss", inv), p.Stop.ToString("yyyy-MM-ddTHH:mm:ss", inv),
                    Clean(p.Title), Clean(p.SubTitle), p.Group, Clean(p.Sub), p.GroupGuessed ? "True" : "False",
                    p.Year.ToString(inv), Clean(p.Episode), Clean(p.WorkKey), p.Desc.Length.ToString(inv)));

        // si confronta per canale (l'ordine dei canali di Sort-Object può differire) e, dentro il canale,
        // ignorando l'ordine tra programmi con lo stesso inizio: RegistraTV li ordinava con List.Sort, non stabile
        static Dictionary<string, List<string>> ByChannel(IEnumerable<string> rows) =>
            rows.GroupBy(l => l.Split('\t')[0]).ToDictionary(x => x.Key, x => x.OrderBy(l => l.Split('\t')[1], StringComparer.Ordinal).ThenBy(l => l, StringComparer.Ordinal).ToList());
        var exp = ByChannel(expected);
        var act = ByChannel(actual);
        var diffs = new List<string>();
        foreach (var ch in exp.Keys.Union(act.Keys))
        {
            var e = exp.GetValueOrDefault(ch) ?? [];
            var a = act.GetValueOrDefault(ch) ?? [];
            for (int i = 0; i < Math.Max(e.Count, a.Count); i++)
            {
                string? x = i < e.Count ? e[i] : null, y = i < a.Count ? a[i] : null;
                if (x != y) diffs.Add($"atteso: {x}\n  avuto: {y}");
            }
        }
        output.WriteLine($"Programmi: {expected.Count} attesi, {actual.Count} letti, {diffs.Count} differenze");
        foreach (var d in diffs.Take(20)) output.WriteLine(d);
        Assert.Equal(expected.Count, actual.Count);
        Assert.Empty(diffs);
    }
}
