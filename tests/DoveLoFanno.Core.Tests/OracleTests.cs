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
    const string Fmt = "yyyy-MM-ddTHH:mm:ss";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

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
        var lines = File.ReadAllLines(Path.Combine(dir, "guide-oracle.tsv"));
        var head = lines[0].Split('\t');
        var before = DateTime.ParseExact(head[1], Fmt, Inv);
        var after = head.Length > 2 ? DateTime.ParseExact(head[2], Fmt, Inv) : before;

        var g = XmltvReader.Load(Path.Combine(dir, "guide.xml.gz"), before);
        var actual = new List<string>();
        foreach (string id in g.ChannelIds)
            foreach (var p in g.Programmes(id))
                actual.Add(string.Join('\t',
                    p.Channel, p.Start.ToString(Fmt, Inv), p.Stop.ToString(Fmt, Inv),
                    Clean(p.Title), Clean(p.SubTitle), p.Group, Clean(p.Sub), p.GroupGuessed ? "True" : "False",
                    p.Year.ToString(Inv), Clean(p.Episode), Clean(p.WorkKey), p.Desc.Length.ToString(Inv)));

        // programmi che finiscono mentre RegistraTV leggeva: scartati o no a seconda dell'istante esatto
        bool Edge(string row)
        {
            var stop = DateTime.ParseExact(row.Split('\t')[2], Fmt, Inv);
            return stop >= before.AddDays(-1) && stop <= after.AddDays(-1);
        }
        var expected = lines.Skip(1).Where(r => !Edge(r)).ToList();
        actual = actual.Where(r => !Edge(r)).ToList();

        // confronto come multiinsiemi: l'ordine dei canali (Sort-Object) e quello tra programmi con lo stesso
        // inizio (List.Sort di RegistraTV non è stabile) non contano
        var missing = Minus(expected, actual);
        var extra = Minus(actual, expected);
        output.WriteLine($"Programmi: {expected.Count} attesi, {actual.Count} letti; mancanti {missing.Count}, in più {extra.Count}");
        foreach (var r in missing.Take(10)) output.WriteLine("mancante: " + r);
        foreach (var r in extra.Take(10)) output.WriteLine("in più:   " + r);
        Assert.True(expected.Count > 1000, "oracolo con troppo pochi programmi");
        Assert.Empty(missing);
        Assert.Empty(extra);
    }

    static List<string> Minus(List<string> a, List<string> b)
    {
        var count = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in b) count[r] = count.GetValueOrDefault(r) + 1;
        var res = new List<string>();
        foreach (var r in a)
        {
            if (count.GetValueOrDefault(r) > 0) count[r]--;
            else res.Add(r);
        }
        return res;
    }
}
