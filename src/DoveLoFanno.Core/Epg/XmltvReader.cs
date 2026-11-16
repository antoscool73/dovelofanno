using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DoveLoFanno.Epg;

/// <summary>
/// Lettura di una guida XMLTV (anche gzip) in una <see cref="EpgGuide"/>. Portata da EpgGuide.Load di RegistraTV:
/// scarta i programmi finiti da più di un giorno, toglie le descrizioni ripetute due volte, calcola la fine
/// mancante dal programma successivo e classifica i generi con id e nomi del canale come indizio.
/// </summary>
public static partial class XmltvReader
{
    public static EpgGuide Load(string path, DateTime now)
    {
        using var fs = File.OpenRead(path);
        return Load(fs, now);
    }

    public static EpgGuide Load(Stream stream, DateTime now)
    {
        var progs = new Dictionary<string, List<EpgProgramme>>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        DateTime keep = now.AddDays(-1);

        Stream input = stream;
        if (stream.CanSeek)
        {
            int b1 = stream.ReadByte();
            int b2 = stream.ReadByte();
            stream.Position = 0;
            if (b1 == 0x1f && b2 == 0x8b) input = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
        }

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = true, IgnoreComments = true };
        using (var r = XmlReader.Create(input, settings))
        {
            r.MoveToContent();
            if (r.LocalName != "tv") throw new InvalidDataException("il file non è una guida XMLTV");
            while (!r.EOF)
            {
                if (r.NodeType != XmlNodeType.Element || (r.LocalName != "channel" && r.LocalName != "programme")) { r.Read(); continue; }
                var e = (XElement)XNode.ReadFrom(r);
                bool isChannel = e.Name.LocalName == "channel";
                string? ch = (string?)e.Attribute(isChannel ? "id" : "channel");
                if (string.IsNullOrEmpty(ch)) continue;
                if (isChannel)
                {
                    if (!names.TryGetValue(ch, out var n)) names[ch] = n = [];
                    foreach (var d in e.Elements("display-name")) n.Add(d.Value);
                    continue;
                }
                var p = new EpgProgramme { Channel = ch };
                try
                {
                    p.Start = ParseTime((string?)e.Attribute("start"));
                    p.Stop = ParseTime((string?)e.Attribute("stop"));
                }
                catch (FormatException) { continue; }
                catch (ArgumentOutOfRangeException) { continue; }
                if (p.Start == DateTime.MinValue) continue;
                if (p.Stop != DateTime.MinValue && p.Stop < keep) continue;
                p.Title = Text(e, "title");
                p.SubTitle = Text(e, "sub-title");
                p.Desc = Undouble(Text(e, "desc"));
                var cats = new List<string>();
                foreach (var c in e.Elements("category"))
                {
                    string v = c.Value.Trim();
                    if (v.Length > 0 && !cats.Contains(v)) cats.Add(v);
                }
                p.Categories = [.. cats];
                p.Category = string.Join(" / ", p.Categories);
                var ym = YearPrefix().Match(Text(e, "date"));
                if (ym.Success) p.Year = int.Parse(ym.Value, CultureInfo.InvariantCulture);
                p.Country = Text(e, "country");
                var cr = e.Element("credits");
                if (cr != null)
                {
                    p.Directors = Join(cr.Elements("director"), 3);
                    p.Actors = Join(cr.Elements("actor"), 6);
                }
                p.Episode = Episode(e);
                p.Icon = (string?)e.Element("icon")?.Attribute("src") ?? "";
                if (!progs.TryGetValue(ch, out var l)) progs[ch] = l = [];
                l.Add(p);
            }
        }

        var keys = new Dictionary<string, string>();
        int count = 0;
        DateTime last = DateTime.MinValue;
        foreach (var id in progs.Keys.ToList())
        {
            // ordinamento stabile: a parità di inizio resta l'ordine del file
            // (RegistraTV usava List.Sort, che tra i pari dava un ordine casuale)
            var l = progs[id] = [.. progs[id].OrderBy(p => p.Start)];
            for (int i = 0; i < l.Count; i++)
            {
                // "stop" è facoltativo in XMLTV: finisce quando inizia il successivo
                if (l[i].Stop == DateTime.MinValue) l[i].Stop = i + 1 < l.Count ? l[i + 1].Start : l[i].Start.AddHours(1);
                if (l[i].Stop > last) last = l[i].Stop;
            }
            count += l.Count;
            var all = new List<string> { id };
            if (names.TryGetValue(id, out var n)) all.AddRange(n);
            string hint = string.Join(" ", all).Replace('.', ' ');
            foreach (var p in l) Genres.Classify(p, hint);
            foreach (string s in all)
            {
                // a parità di nome vince il canale con più programmi
                string k = Titles.ChannelKey(s);
                if (k.Length == 0) continue;
                if (!keys.TryGetValue(k, out var old) || progs[old].Count < l.Count) keys[k] = id;
            }
        }
        if (count == 0) throw new InvalidDataException("la guida non contiene programmi");
        var display = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, n) in names)
            if (n.Count > 0 && n[0].Trim().Length > 0) display[id] = n[0].Trim();
        return new EpgGuide(progs, keys, last, display);
    }

    // onscreen vince; xmltv_ns "stagione.episodio.parte" è contato da 0
    static string Episode(XElement e)
    {
        string result = "";
        foreach (var en in e.Elements("episode-num"))
        {
            string? sys = (string?)en.Attribute("system");
            string v = en.Value.Trim();
            if (v.Length == 0) continue;
            if (sys == "onscreen") return v;
            if (sys == "xmltv_ns")
            {
                string[] k = v.Split('.');
                string se = k.Length > 0 && int.TryParse(k[0].Split('/')[0].Trim(), out int s) ? "S" + (s + 1) : "";
                string ep = k.Length > 1 && int.TryParse(k[1].Split('/')[0].Trim(), out int n) ? "E" + (n + 1) : "";
                if (se.Length + ep.Length > 0) result = (se + " " + ep).Trim();
            }
        }
        return result;
    }

    static string Text(XElement e, string name)
    {
        XElement? best = null;
        foreach (var x in e.Elements(name))
        {
            best ??= x;
            if ((string?)x.Attribute("lang") == "it") { best = x; break; }
        }
        return best == null ? "" : best.Value.Trim();
    }

    static string Join(IEnumerable<XElement> xs, int max)
    {
        var l = new List<string>();
        foreach (var x in xs)
        {
            string v = x.Value.Trim();
            if (v.Length > 0 && !l.Contains(v)) l.Add(v);
            if (l.Count >= max) break;
        }
        return string.Join(", ", l);
    }

    // alcune guide ripetono la descrizione due volte di seguito
    internal static string Undouble(string s)
    {
        int h = s.Length / 2;
        if (s.Length > 20 && s.Length % 2 == 1 && s[h] == ' ' && string.CompareOrdinal(s, 0, s, h + 1, h) == 0) return s[..h];
        return s;
    }

    /// <summary>Orario XMLTV ("20261116203000 +0100") in ora locale; vuoto = DateTime.MinValue.</summary>
    internal static DateTime ParseTime(string? s)
    {
        if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
        s = s.Trim();
        int i = 0;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        string d = s[..i];
        if (d.Length < 12) throw new FormatException("orario non valido: " + s);
        d = d.Length < 14 ? d.PadRight(14, '0') : d[..14];
        var t = DateTime.ParseExact(d, "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var off = TimeSpan.Zero;
        string z = s[i..].Trim();
        if (z.Length >= 5 && (z[0] == '+' || z[0] == '-'))
        {
            off = new TimeSpan(int.Parse(z.Substring(1, 2), CultureInfo.InvariantCulture), int.Parse(z.Substring(3, 2), CultureInfo.InvariantCulture), 0);
            if (z[0] == '-') off = off.Negate();
        }
        return new DateTimeOffset(t, off).LocalDateTime;
    }

    [GeneratedRegex(@"^\d{4}")]
    private static partial Regex YearPrefix();
}
