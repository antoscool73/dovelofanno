// Sorgente C# di RegistraTV (RegistraTV.ps1, variabile $EpgSource) al tag registratv-v1.0 (commit 5a19df6).
// Serve solo come oracolo dei test: non modificare.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace RegistraTV
{
    public class EpgProgramme
    {
        public string Channel;
        public DateTime Start;
        public DateTime Stop;
        public string Title = "";
        public string SubTitle = "";
        public string Desc = "";
        public string Category = "";
        public string[] Categories = new string[0];
        public int Year;
        public string Country = "";
        public string Directors = "";
        public string Actors = "";
        public string Episode = "";
        // gruppo fisso (Film, Serie TV, Documentari, Notizie, Sport, Intrattenimento, Bambini, Altro)
        // e sottogenere (per i film: Commedia, Drammatico...)
        public string Group = "Altro";
        public string Sub = "";
        public bool GroupGuessed;

        public string Genre
        {
            get { return Sub.Length > 0 ? Group + " · " + Sub : Group; }
        }

        // stessa opera: i film per titolo e anno, il resto per titolo ed episodio (o sottotitolo);
        // senza episodio ogni passaggio e' a se' (non si possono riconoscere le repliche)
        public string WorkKey
        {
            get
            {
                string t = EpgGuide.TitleKey(Title);
                if (Group == "Film") return t + "|" + Year;
                string ep = Episode.Length > 0 ? Genres.Plain(Episode) : EpgGuide.TitleKey(SubTitle);
                if (ep.Length == 0) ep = "@" + Start.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
                return t + "|" + ep;
            }
        }

        public string TitleKey
        {
            get { return EpgGuide.TitleKey(Title); }
        }
    }

    // classificazione dei generi della guida nei gruppi fissi
    public static class Genres
    {
        public static readonly string[] Groups = { "Film", "Serie TV", "Documentari", "Notizie", "Sport", "Intrattenimento", "Bambini", "Altro" };

        static readonly string[] Kids = { "cartoni animati", "cartoni", "bambini", "per i bimbi piccoli", "anime", "ragazzi", "kids" };
        static readonly string[] Series = { "serie tv", "serie", "fiction", "telefilm", "soap", "soap opera", "sitcom", "miniserie", "telenovela" };
        static readonly string[] Sport = { "sport", "calcio", "tennis", "motori", "basket", "pallavolo", "volley", "equestri", "ciclismo", "rugby", "golf", "atletica", "nuoto", "sci", "sport invernali", "boxe", "wrestling", "football americano", "baseball", "pallamano", "sport estremi", "formula 1", "motociclismo" };
        static readonly string[] News = { "notiziario", "news", "telegiornale", "tg", "informazione", "economia", "attualita", "politica", "meteo", "approfondimento", "inchiesta", "rubrica" };
        static readonly string[] Docs = { "documentario", "documentari", "natura", "storia", "arte e cultura", "scoperta e avventura", "scienza", "scienze", "viaggi", "societa", "cultura", "animali", "tecnologia", "mondo e tendenze" };
        static readonly string[] Fun = { "intrattenimento", "show", "talk show", "gioco", "quiz", "reality", "reality show", "varieta", "musica", "cucina", "magazine", "caccia", "pesca", "moda", "lifestyle", "game show", "concerto", "teatro", "ragazzi e musica" };
        // categorie generiche che non dicono il sottogenere di un film
        static readonly string[] NoSub = { "film", "cinema", "altri", "altro", "altri programmi" };

        public static string Plain(string s)
        {
            if (s == null) return "";
            string d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder b = new StringBuilder();
            foreach (char c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) b.Append(c);
            // l'apostrofo separa le parole: "L' Amica" e "L'amica" -> "l amica"
            return b.ToString().Replace("'", " ").Replace("’", " ");
        }

        static bool Any(List<string> cats, string[] set)
        {
            foreach (string c in cats) if (Array.IndexOf(set, c) >= 0) return true;
            return false;
        }

        // hint: id e nomi del canale nella guida, per i programmi senza genere
        public static void Classify(EpgProgramme p, string hint)
        {
            List<string> cats = new List<string>();
            foreach (string c in p.Categories) cats.Add(Plain(c));
            string g = null;
            string sub = "";
            bool hasEp = p.Episode.Length > 0;
            double mins = (p.Stop - p.Start).TotalMinutes;
            if (cats.Contains("film"))
            {
                if (hasEp) g = "Serie TV";
                else if (mins < 20) g = "Altro";
                else
                {
                    g = "Film";
                    for (int i = 0; i < cats.Count; i++)
                        if (Array.IndexOf(NoSub, cats[i]) < 0) { sub = Cap(p.Categories[i]); break; }
                }
            }
            else if (cats.Contains("informazione") && cats.Contains("sport")) g = "Sport";
            else if (Any(cats, Kids)) g = "Bambini";
            else if (Any(cats, Series)) g = "Serie TV";
            else if (Any(cats, Sport)) g = "Sport";
            else if (cats.Contains("documentario") || cats.Contains("documentari")) g = "Documentari";
            else if (Any(cats, News)) g = "Notizie";
            else if (cats.Contains("talk show")) g = "Intrattenimento";
            else if (Any(cats, Docs) && !cats.Contains("cucina") && !cats.Contains("caccia") && !cats.Contains("pesca") && !cats.Contains("musica")) g = "Documentari";
            else if (Any(cats, Fun)) g = "Intrattenimento";
            else if (cats.Count > 0)
            {
                // sottogeneri da film senza la parola "Film" (alcune fonti scrivono solo "Commedia")
                string[] filmSubs = { "commedia", "drammatico", "azione", "thriller", "horror", "fantascienza", "western", "poliziesco", "giallo", "sentimentale", "avventura", "fantastico", "guerra", "biografico", "storico", "musicale", "noir", "animazione", "comico", "erotico", "grottesco", "spionaggio", "catastrofico", "mistero" };
                if (Any(cats, filmSubs) && !hasEp && mins >= 70) { g = "Film"; sub = Cap(p.Categories[0]); }
                else if (Any(cats, filmSubs)) g = "Serie TV";
            }
            if (g == null)
            {
                g = Guess(p, hint);
                p.GroupGuessed = true;
            }
            p.Group = g;
            p.Sub = sub;
        }

        static string Cap(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        // programmi senza genere: dal titolo e dal canale
        static string Guess(EpgProgramme p, string hint)
        {
            string t = Plain(p.Title);
            if (Regex.IsMatch(t, @"^(tg\s?\d*|tg[a-z0-9]+|telegiornale|notiziario|meteo|rassegna stampa|news\b|edicola)") || Regex.IsMatch(t, @"\b(tg[0-9]?|telegiornale|giornale radio|rainews|rainews24|tgcom24|gr\s?[0-9])\b")) return "Notizie";
            string h = Plain(hint);
            if (Regex.IsMatch(h, @"movie|cinema|cine|iris|film")) return p.Episode.Length > 0 ? "Serie TV" : (p.Stop - p.Start).TotalMinutes >= 60 ? "Film" : "Altro";
            if (Regex.IsMatch(h, @"news|tg24|tgcom|tgnorba|euronews|rainews|parlamento")) return "Notizie";
            if (Regex.IsMatch(h, @"sport|eurosport|supertennis|motor|calcio|bike")) return "Sport";
            if (Regex.IsMatch(h, @"kids|boing|cartoon|frisbee|\bk2\b|yoyo|gulp|nick|super!|deakids|cartoonito|boomerang")) return "Bambini";
            if (Regex.IsMatch(h, @"storia|focus|doc|nature|natura|geo|history|scienz")) return "Documentari";
            if (Regex.IsMatch(h, @"crime|giallo|topcrime|fiction|serie|premium")) return p.Episode.Length > 0 || p.SubTitle.Length > 0 ? "Serie TV" : "Altro";
            if (p.Episode.Length > 0) return "Serie TV";
            return "Altro";
        }
    }

    public class EpgGuide
    {
        // nomi della playlist che la normalizzazione non basta ad abbinare
        static readonly Dictionary<string, string> Alias = new Dictionary<string, string> {
            { "mediaset20", "20" }, { "cielotv", "cielo" }, { "mediaset27twentyseven", "27twentyseven" },
            { "tgcom24", "tgcom" }, { "rtl1025tv", "rtl1025" }, { "radio105tv", "radio105" },
            { "radiomontecarlo", "rmc" }, { "radiofrecciatv", "radiofreccia" }, { "discovery", "discoverychannel" }
        };

        readonly string url;
        readonly string cacheFile;
        readonly double maxAgeHours;
        Dictionary<string, List<EpgProgramme>> byId = new Dictionary<string, List<EpgProgramme>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> byKey = new Dictionary<string, string>();

        public volatile bool Done;
        public string Error = "";
        public string Note = "";
        public DateTime FileTime;
        public DateTime LastStop = DateTime.MinValue;
        public int ChannelCount;
        public int ProgrammeCount;

        public EpgGuide(string url, string cacheFile, double maxAgeHours)
        {
            this.url = url == null ? "" : url;
            this.cacheFile = cacheFile;
            this.maxAgeHours = maxAgeHours;
        }

        public void Start()
        {
            Thread t = new Thread(Run);
            t.IsBackground = true;
            t.Start();
        }

        void Run()
        {
            try
            {
                bool fresh = File.Exists(cacheFile) && (DateTime.Now - File.GetLastWriteTime(cacheFile)).TotalHours < maxAgeHours;
                bool loaded = false;
                if (!fresh && url.Length > 0)
                {
                    // la copia salvata viene sostituita solo se il file nuovo si legge
                    string tmp = cacheFile + ".tmp";
                    try
                    {
                        Download(tmp);
                        Load(tmp);
                        if (File.Exists(cacheFile)) File.Delete(cacheFile);
                        File.Move(tmp, cacheFile);
                        loaded = true;
                    }
                    catch (Exception ex)
                    {
                        try { File.Delete(tmp); } catch { }
                        if (!File.Exists(cacheFile)) throw;
                        Note = "non aggiornata: " + ex.Message;
                    }
                }
                if (!File.Exists(cacheFile)) throw new Exception("guida non scaricata");
                if (!loaded) Load(cacheFile);
                FileTime = File.GetLastWriteTime(cacheFile);
            }
            catch (Exception ex) { Error = ex.Message; }
            Done = true;
        }

        void Download(string path)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "RegistraTV";
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            using (WebResponse resp = req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (FileStream f = File.Create(path))
            {
                s.CopyTo(f);
            }
        }

        void Load(string path)
        {
            Dictionary<string, List<EpgProgramme>> progs = new Dictionary<string, List<EpgProgramme>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> names = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            DateTime keep = DateTime.Now.AddDays(-1);
            using (FileStream fs = File.OpenRead(path))
            {
                int b1 = fs.ReadByte();
                int b2 = fs.ReadByte();
                fs.Position = 0;
                Stream input = fs;
                if (b1 == 0x1f && b2 == 0x8b) input = new GZipStream(fs, CompressionMode.Decompress);
                XmlReaderSettings st = new XmlReaderSettings();
                st.DtdProcessing = DtdProcessing.Ignore;
                st.IgnoreWhitespace = true;
                st.IgnoreComments = true;
                using (XmlReader r = XmlReader.Create(input, st))
                {
                    r.MoveToContent();
                    if (r.LocalName != "tv") throw new Exception("il file non è una guida XMLTV");
                    while (!r.EOF)
                    {
                        if (r.NodeType != XmlNodeType.Element || (r.LocalName != "channel" && r.LocalName != "programme")) { r.Read(); continue; }
                        XElement e = (XElement)XNode.ReadFrom(r);
                        bool isChannel = e.Name.LocalName == "channel";
                        string ch = (string)e.Attribute(isChannel ? "id" : "channel");
                        if (string.IsNullOrEmpty(ch)) continue;
                        if (isChannel)
                        {
                            List<string> n;
                            if (!names.TryGetValue(ch, out n)) { n = new List<string>(); names[ch] = n; }
                            foreach (XElement d in e.Elements("display-name")) n.Add(d.Value);
                            continue;
                        }
                        EpgProgramme p = new EpgProgramme();
                        p.Channel = ch;
                        try
                        {
                            p.Start = ParseTime((string)e.Attribute("start"));
                            p.Stop = ParseTime((string)e.Attribute("stop"));
                        }
                        catch { continue; }
                        if (p.Start == DateTime.MinValue) continue;
                        if (p.Stop != DateTime.MinValue && p.Stop < keep) continue;
                        p.Title = Text(e, "title");
                        p.SubTitle = Text(e, "sub-title");
                        p.Desc = Undouble(Text(e, "desc"));
                        List<string> cats = new List<string>();
                        foreach (XElement c in e.Elements("category"))
                        {
                            string v = c.Value.Trim();
                            if (v.Length > 0 && !cats.Contains(v)) cats.Add(v);
                        }
                        p.Categories = cats.ToArray();
                        p.Category = string.Join(" / ", p.Categories);
                        Match ym = Regex.Match(Text(e, "date"), @"^\d{4}");
                        if (ym.Success) p.Year = int.Parse(ym.Value);
                        p.Country = Text(e, "country");
                        XElement cr = e.Element("credits");
                        if (cr != null)
                        {
                            p.Directors = Join(cr.Elements("director"), 3);
                            p.Actors = Join(cr.Elements("actor"), 6);
                        }
                        foreach (XElement en in e.Elements("episode-num"))
                        {
                            string sys = (string)en.Attribute("system");
                            string v = en.Value.Trim();
                            if (v.Length == 0) continue;
                            if (sys == "onscreen") { p.Episode = v; break; }
                            if (sys == "xmltv_ns")
                            {
                                // "stagione.episodio.parte" contati da 0
                                string[] k = v.Split('.');
                                int s, n;
                                string se = k.Length > 0 && int.TryParse(k[0].Split('/')[0].Trim(), out s) ? "S" + (s + 1) : "";
                                string ep = k.Length > 1 && int.TryParse(k[1].Split('/')[0].Trim(), out n) ? "E" + (n + 1) : "";
                                if (se.Length + ep.Length > 0) p.Episode = (se + " " + ep).Trim();
                            }
                        }
                        List<EpgProgramme> l;
                        if (!progs.TryGetValue(ch, out l)) { l = new List<EpgProgramme>(); progs[ch] = l; }
                        l.Add(p);
                    }
                }
            }

            Dictionary<string, string> keys = new Dictionary<string, string>();
            int count = 0;
            DateTime last = DateTime.MinValue;
            foreach (KeyValuePair<string, List<EpgProgramme>> kv in progs)
            {
                List<EpgProgramme> l = kv.Value;
                l.Sort((a, b) => a.Start.CompareTo(b.Start));
                for (int i = 0; i < l.Count; i++)
                {
                    // "stop" e' facoltativo in XMLTV: finisce quando inizia il successivo
                    if (l[i].Stop == DateTime.MinValue) l[i].Stop = (i + 1 < l.Count) ? l[i + 1].Start : l[i].Start.AddHours(1);
                    if (l[i].Stop > last) last = l[i].Stop;
                }
                count += l.Count;
                List<string> all = new List<string>();
                all.Add(kv.Key);
                List<string> n;
                if (names.TryGetValue(kv.Key, out n)) all.AddRange(n);
                string hint = string.Join(" ", all.ToArray()).Replace('.', ' ');
                foreach (EpgProgramme p in l) Genres.Classify(p, hint);
                foreach (string s in all)
                {
                    // a parita' di nome vince il canale con piu' programmi
                    string k = Norm(s);
                    if (k.Length == 0) continue;
                    string old;
                    if (!keys.TryGetValue(k, out old) || progs[old].Count < l.Count) keys[k] = kv.Key;
                }
            }
            if (count == 0) throw new Exception("la guida non contiene programmi");
            byId = progs;
            byKey = keys;
            ProgrammeCount = count;
            ChannelCount = progs.Count;
            LastStop = last;
        }

        static string Text(XElement e, string name)
        {
            XElement best = null;
            foreach (XElement x in e.Elements(name))
            {
                if (best == null) best = x;
                if ((string)x.Attribute("lang") == "it") { best = x; break; }
            }
            return best == null ? "" : best.Value.Trim();
        }

        static string Join(IEnumerable<XElement> xs, int max)
        {
            List<string> l = new List<string>();
            foreach (XElement x in xs)
            {
                string v = x.Value.Trim();
                if (v.Length > 0 && !l.Contains(v)) l.Add(v);
                if (l.Count >= max) break;
            }
            return string.Join(", ", l.ToArray());
        }

        // titolo per confronti: minuscole, senza accenti, punteggiatura e articoli iniziali
        public static string TitleKey(string s)
        {
            string t = Genres.Plain(s);
            t = Regex.Replace(t, @"[^a-z0-9]+", " ").Trim();
            t = Regex.Replace(t, @"^(il|lo|la|i|gli|le|l|un|uno|una|the|a|an)\s+", "");
            return t;
        }

        // alcune guide ripetono la descrizione due volte di seguito
        static string Undouble(string s)
        {
            int h = s.Length / 2;
            if (s.Length > 20 && s.Length % 2 == 1 && s[h] == ' ' && string.CompareOrdinal(s, 0, s, h + 1, h) == 0) return s.Substring(0, h);
            return s;
        }

        static DateTime ParseTime(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
            s = s.Trim();
            int i = 0;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            string d = s.Substring(0, i);
            if (d.Length < 12) throw new FormatException("orario non valido: " + s);
            d = d.Length < 14 ? d.PadRight(14, '0') : d.Substring(0, 14);
            DateTime t = DateTime.ParseExact(d, "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            TimeSpan off = TimeSpan.Zero;
            string z = s.Substring(i).Trim();
            if (z.Length >= 5 && (z[0] == '+' || z[0] == '-'))
            {
                off = new TimeSpan(int.Parse(z.Substring(1, 2)), int.Parse(z.Substring(3, 2)), 0);
                if (z[0] == '-') off = off.Negate();
            }
            return new DateTimeOffset(t, off).LocalDateTime;
        }

        // "Canale.5.it", "Rai 1 HD  101", "TV8.HD.it" -> "canale5", "rai1", "tv8"
        public static string Norm(string s)
        {
            if (s == null) return "";
            s = s.ToLowerInvariant().Trim();
            s = Regex.Replace(s, @"\.it$", "");
            s = Regex.Replace(s, @"[.\s]{2,}\d+$", "");
            s = Regex.Replace(s, @"\b(hd|fhd|uhd)\b", "");
            return Regex.Replace(s, "[^a-z0-9]", "");
        }

        public string Resolve(string tvgId, string name)
        {
            if (!string.IsNullOrEmpty(tvgId) && byId.ContainsKey(tvgId)) return tvgId;
            foreach (string s in new string[] { tvgId, name })
            {
                string k = Norm(s);
                if (k.Length == 0) continue;
                string id;
                if (byKey.TryGetValue(k, out id)) return id;
                string a;
                if (Alias.TryGetValue(k, out a) && byKey.TryGetValue(a, out id)) return id;
            }
            return null;
        }

        // programmi del canale che si sovrappongono a [from, to), filtrati per testo (senza badare a maiuscole e accenti)
        public List<EpgProgramme> Find(string id, string text, DateTime from, DateTime to)
        {
            List<EpgProgramme> res = new List<EpgProgramme>();
            List<EpgProgramme> list;
            if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out list)) return res;
            foreach (EpgProgramme p in list)
            {
                if (p.Stop <= from || p.Start >= to) continue;
                if (!string.IsNullOrEmpty(text) && !Has(p.Title, text) && !Has(p.SubTitle, text)) continue;
                res.Add(p);
            }
            return res;
        }

        static bool Has(string s, string t)
        {
            return s != null && CultureInfo.InvariantCulture.CompareInfo.IndexOf(s, t, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
        }

        // voti dei film (facoltativo): lo imposta la GUI
        public RatingService Ratings;

        public double RatingOf(EpgProgramme p)
        {
            RatingService r = Ratings;
            if (r == null || p.Group != "Film") return 0;
            RatingInfo i = r.Lookup(p);
            return i == null ? 0 : i.Value;
        }

        // ricerca su piu' canali: testo nel titolo, sottotitolo, regista o attori; gruppo e sottogenere
        // (vuoti = tutti); voto minimo (0 = nessun filtro). Ordinata per orario, al massimo "max" risultati
        public List<EpgProgramme> Search(string[] ids, string text, string group, string sub, double minRating, DateTime from, DateTime to, int max)
        {
            List<EpgProgramme> res = new List<EpgProgramme>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in ids)
            {
                List<EpgProgramme> list;
                if (string.IsNullOrEmpty(id) || !seen.Add(id) || !byId.TryGetValue(id, out list)) continue;
                foreach (EpgProgramme p in list)
                {
                    if (p.Stop <= from || p.Start >= to) continue;
                    if (!string.IsNullOrEmpty(group) && p.Group != group) continue;
                    if (!string.IsNullOrEmpty(sub) && !string.Equals(p.Sub, sub, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(text) && !Has(p.Title, text) && !Has(p.SubTitle, text) && !Has(p.Directors, text) && !Has(p.Actors, text)) continue;
                    if (minRating > 0 && RatingOf(p) < minRating) continue;
                    res.Add(p);
                }
            }
            res.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : string.CompareOrdinal(a.Channel, b.Channel));
            if (max > 0 && res.Count > max) res.RemoveRange(max, res.Count - max);
            return res;
        }

        // sottogeneri dei film presenti nella guida, dal piu' frequente
        public List<string> FilmSubs(int minCount)
        {
            Dictionary<string, int> n = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (List<EpgProgramme> l in byId.Values)
                foreach (EpgProgramme p in l)
                    if (p.Group == "Film" && p.Sub.Length > 0) { int k; n.TryGetValue(p.Sub, out k); n[p.Sub] = k + 1; }
            List<string> res = new List<string>();
            foreach (KeyValuePair<string, int> kv in n) if (kv.Value >= minCount) res.Add(kv.Key);
            res.Sort(StringComparer.CurrentCultureIgnoreCase);
            return res;
        }

        // film (non gia' finiti) dei canali indicati: da passare al servizio dei voti
        public List<EpgProgramme> Films(string[] ids)
        {
            List<EpgProgramme> res = new List<EpgProgramme>();
            DateTime now = DateTime.Now;
            foreach (string id in ids)
            {
                List<EpgProgramme> list;
                if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out list)) continue;
                foreach (EpgProgramme p in list) if (p.Group == "Film" && p.Stop > now) res.Add(p);
            }
            return res;
        }
    }

    public class RatingInfo
    {
        public double Value;            // voto mostrato: IMDb se c'e', altrimenti TMDB
        public string Source = "";      // "IMDb" o "TMDB"
        public int Votes;
        public string ImdbId = "";
        public int TmdbId;
        public string OriginalTitle = "";
        public int Year;
    }

    // Voti dei film: TMDB (chiave gratuita, uso personale) trova il film dal titolo italiano e
    // dall'anno e da' il codice IMDb; il voto IMDb viene dal file ufficiale title.ratings.tsv.gz
    // (scaricato al massimo una volta a settimana). Tutto in un thread, con cache su disco:
    // ogni giorno si cercano solo i titoli nuovi.
    public class RatingService
    {
        class Entry
        {
            public bool Found;
            public int TmdbId;
            public string ImdbId = "";
            public string OriginalTitle = "";
            public int Year;
            public double TmdbVote;
            public int TmdbCount;
            public double ImdbVote;
            public int ImdbVotes;
            public DateTime Checked;
            public DateTime ImdbChecked;
            public int Ver = MatchVersion;
        }

        // versione delle regole di abbinamento (2 = 14/11/2026: omonimi sconosciuti e anno sbagliato)
        const int MatchVersion = 2;

        class Pend
        {
            public string Key;
            public string Title;
            public int Year;
        }

        const string Api = "https://api.themoviedb.org/3/";
        const string ImdbUrl = "https://datasets.imdbws.com/title.ratings.tsv.gz";
        readonly string apiKey;
        readonly string dir;
        readonly string cacheFile;
        readonly string imdbFile;
        readonly object sync = new object();
        readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();
        readonly Queue<Pend> queue = new Queue<Pend>();
        readonly HashSet<string> queued = new HashSet<string>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        Thread worker;
        volatile bool stop;

        public volatile int Version;
        public volatile string Status = "";
        public volatile string Error = "";

        public RatingService(string apiKey, string dir)
        {
            this.apiKey = (apiKey ?? "").Trim();
            this.dir = dir;
            Directory.CreateDirectory(dir);
            cacheFile = Path.Combine(dir, "cache.tsv");
            imdbFile = Path.Combine(dir, "title.ratings.tsv.gz");
            LoadCache();
        }

        public int Pending
        {
            get { lock (sync) { return queue.Count; } }
        }

        public void Stop()
        {
            stop = true;
            wake.Set();
        }

        static string KeyOf(string title, int year)
        {
            return EpgGuide.TitleKey(title) + "|" + year;
        }

        public RatingInfo Lookup(EpgProgramme p)
        {
            Entry e;
            lock (sync) { if (!cache.TryGetValue(KeyOf(p.Title, p.Year), out e)) return null; }
            if (!e.Found) return null;
            RatingInfo r = new RatingInfo();
            r.ImdbId = e.ImdbId;
            r.TmdbId = e.TmdbId;
            r.OriginalTitle = e.OriginalTitle;
            r.Year = e.Year;
            if (e.ImdbVote > 0) { r.Value = e.ImdbVote; r.Votes = e.ImdbVotes; r.Source = "IMDb"; }
            else if (e.TmdbVote > 0 && e.TmdbCount >= 20) { r.Value = e.TmdbVote; r.Votes = e.TmdbCount; r.Source = "TMDB"; }
            else return null;
            return r;
        }

        // "" = cercato e non trovato; null = non ancora cercato
        public string State(EpgProgramme p)
        {
            Entry e;
            lock (sync)
            {
                if (cache.TryGetValue(KeyOf(p.Title, p.Year), out e)) return e.Found ? "trovato" : "";
                return null;
            }
        }

        // mette in coda i film non ancora cercati (quelli non trovati si riprovano dopo 30 giorni)
        public void Request(IEnumerable<EpgProgramme> films)
        {
            int added = 0;
            lock (sync)
            {
                foreach (EpgProgramme p in films)
                {
                    string k = KeyOf(p.Title, p.Year);
                    if (k.StartsWith("|") || queued.Contains(k)) continue;
                    Entry e;
                    // le voci senza voto (non trovate, o omonimo sconosciuto) cercate con regole di abbinamento
                    // piu' vecchie si riprovano una volta
                    if (cache.TryGetValue(k, out e) && (e.Found || (DateTime.Now - e.Checked).TotalDays < 30)
                        && !(e.ImdbVote <= 0 && e.TmdbCount < 20 && e.Ver < MatchVersion)) continue;
                    Pend r = new Pend();
                    r.Key = k; r.Title = p.Title; r.Year = p.Year;
                    queue.Enqueue(r);
                    queued.Add(k);
                    added++;
                }
            }
            if (worker == null)
            {
                worker = new Thread(Run);
                worker.IsBackground = true;
                worker.Start();
            }
            wake.Set();
        }

        void Run()
        {
            while (!stop)
            {
                try { Work(); }
                catch (Exception ex) { Error = ex.Message; }
                wake.WaitOne(TimeSpan.FromHours(6));
            }
        }

        void Work()
        {
            int done = 0;
            while (!stop && apiKey.Length > 0)
            {
                Pend r;
                lock (sync)
                {
                    if (queue.Count == 0) break;
                    r = queue.Peek();
                }
                Status = "Ricerca dei voti: " + (Pending) + " film da cercare";
                Entry e = Resolve(r.Title, r.Year);
                lock (sync)
                {
                    queue.Dequeue();
                    queued.Remove(r.Key);
                    cache[r.Key] = e;
                }
                done++;
                if (done % 10 == 0) { SaveCache(); Version++; }
                Thread.Sleep(120);
            }
            Error = "";
            UpdateImdb();
            SaveCache();
            Version++;
            int found = 0;
            lock (sync) { foreach (Entry e in cache.Values) if (e.Found) found++; }
            Status = apiKey.Length == 0 ? "Manca la chiave TMDB (Impostazioni)" : "Voti pronti: " + found + " film trovati";
        }

        // ---- TMDB

        // parole del titolo senza articoli, congiunzioni e preposizioni brevi, in ordine:
        // "Io e Marley" = "Io & Marley", "Sotto assedio - White House down" = "White House Down - Sotto Assedio"
        static readonly HashSet<string> StopWords = new HashSet<string> { "e", "and", "il", "lo", "la", "i", "gli", "le", "l", "un", "uno", "una", "the", "a", "an", "of", "di", "da", "del", "della", "dei", "de", "in" };

        static List<string> Words(string s)
        {
            List<string> w = new List<string>();
            foreach (string x in EpgGuide.TitleKey(s).Split(' ')) if (x.Length > 0 && !StopWords.Contains(x) && !w.Contains(x)) w.Add(x);
            w.Sort(StringComparer.Ordinal);
            return w;
        }

        // 2 = stesso titolo, 1 = uno contiene l'altro (titoli Mediaset che uniscono originale e italiano,
        // "Agente 007 - Zona pericolo" / "007 - Zona pericolo"), 0 = diverso
        static int Match(List<string> a, List<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0;
            bool aInB = true, bInA = true;
            foreach (string x in a) if (!b.Contains(x)) { aInB = false; break; }
            foreach (string x in b) if (!a.Contains(x)) { bInA = false; break; }
            if (aInB && bInA) return 2;
            int small = Math.Min(a.Count, b.Count);
            if ((aInB || bInA) && small >= 2 && Math.Abs(a.Count - b.Count) <= 3) return 1;
            return 0;
        }

        Entry Resolve(string title, int year)
        {
            Entry e = new Entry();
            e.Checked = DateTime.Now;
            // "Le sorelle Macaluso (film)" -> "Le sorelle Macaluso"
            string q = Regex.Replace(title, @"\s*\((film|tv|\d{4})\)\s*$", "", RegexOptions.IgnoreCase).Trim();
            List<string> tw = Words(q);
            Dictionary<string, object> best = Pick(SearchMovie(q, year), tw, year);
            // l'anno della guida a volte e' sbagliato (Rai Movie mette spesso 2019): si riprova senza
            if (best == null && year > 0) best = Pick(SearchMovie(q, 0), tw, year);
            // con l'anno sbagliato si trova a volte un omonimo sconosciuto di quell'anno (Phenomenon "2019"
            // = un film con 0 voti, quello trasmesso e' del 1996): se esiste un film con lo stesso titolo
            // e molti voti, e' quasi sempre quello giusto
            else if (best != null && year > 0 && Num(best, "vote_count") < 20)
            {
                Dictionary<string, object> famous = Pick(SearchMovie(q, 0), tw, 0);
                if (famous != null && Num(famous, "vote_count") >= 100 && TitleMatch(famous, tw) == 2) best = famous;
            }
            if (best == null) return e;
            e.Found = true;
            e.TmdbId = (int)Num(best, "id");
            e.OriginalTitle = Str(best, "original_title");
            e.Year = YearOf(Str(best, "release_date"));
            e.TmdbVote = Num(best, "vote_average");
            e.TmdbCount = (int)Num(best, "vote_count");
            try
            {
                Dictionary<string, object> x = Get("movie/" + e.TmdbId + "/external_ids", "") as Dictionary<string, object>;
                if (x != null) e.ImdbId = Str(x, "imdb_id");
            }
            catch (WebException) { }
            return e;
        }

        static bool AllIn(List<string> a, List<string> b)
        {
            if (a.Count == 0) return false;
            foreach (string x in a) if (!b.Contains(x)) return false;
            return true;
        }

        // 2 = titolo (italiano o originale) uguale, 1 = contenuto, 0 = diverso
        static int TitleMatch(Dictionary<string, object> m, List<string> tw)
        {
            return Math.Max(Match(tw, Words(Str(m, "title"))), Match(tw, Words(Str(m, "original_title"))));
        }

        static Dictionary<string, object> Pick(object[] res, List<string> tw, int year)
        {
            // titolo uguale vale piu' di titolo contenuto; l'anno giusto (+-1) da' un bonus forte;
            // a parita' vince il film con piu' voti
            Dictionary<string, object> best = null;
            double bestScore = double.MinValue;
            foreach (object o in res)
            {
                Dictionary<string, object> m = o as Dictionary<string, object>;
                if (m == null) continue;
                int k = TitleMatch(m, tw);
                int ry = YearOf(Str(m, "release_date"));
                bool yearOk = year > 0 && ry > 0 && Math.Abs(ry - year) <= 1;
                bool known = Num(m, "vote_count") >= 20;
                // titolo di una parola contenuto in quello di un film noto dell'anno giusto:
                // "Chase" (2022) = "Chase - Scomparsa" (2022)
                if (k == 0 && yearOk && known && (AllIn(tw, Words(Str(m, "title"))) || AllIn(tw, Words(Str(m, "original_title"))))) k = 1;
                if (k == 0) continue;
                // titolo solo contenuto: serve l'anno giusto, oppure la guida non ha l'anno (Mediaset)
                if (k == 1 && !yearOk && year > 0) continue;
                // un film noto (almeno 20 voti) batte un omonimo esatto sconosciuto: "Chase - Scomparsa" (2022,
                // 1127 voti) e non "Chase" (2022, 1 voto)
                double score = (k == 2 ? 20 : 10) + (yearOk ? 30 : 0) + (year > 0 && ry == year ? 2 : 0) + (known ? 15 : 0);
                score = score * 100000 + Math.Min(99999, Num(m, "vote_count"));
                if (score > bestScore) { bestScore = score; best = m; }
            }
            return best;
        }

        object[] SearchMovie(string title, int year)
        {
            string q = "query=" + Uri.EscapeDataString(title) + "&language=it-IT&include_adult=false";
            if (year > 0) q += "&year=" + year;
            Dictionary<string, object> d = Get("search/movie", q) as Dictionary<string, object>;
            object r;
            if (d != null && d.TryGetValue("results", out r) && r is object[]) return (object[])r;
            return new object[0];
        }

        object Get(string path, string query)
        {
            return Get(apiKey, path, query);
        }

        static object Get(string key, string path, string query)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            // chiave v3 (32 caratteri) come parametro, token v4 (lungo) come Bearer
            bool bearer = key.Length > 40;
            string url = Api + path + "?" + query;
            if (!bearer) url += (query.Length > 0 ? "&" : "") + "api_key=" + Uri.EscapeDataString(key);
            for (int attempt = 0; ; attempt++)
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "RegistraTV";
                req.Accept = "application/json";
                req.Timeout = 20000;
                if (bearer) req.Headers["Authorization"] = "Bearer " + key;
                try
                {
                    using (WebResponse resp = req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        JavaScriptSerializer js = new JavaScriptSerializer();
                        js.MaxJsonLength = 8 * 1024 * 1024;
                        return js.DeserializeObject(sr.ReadToEnd());
                    }
                }
                catch (WebException ex)
                {
                    HttpWebResponse hr = ex.Response as HttpWebResponse;
                    int code = hr == null ? 0 : (int)hr.StatusCode;
                    if (code == 401) throw new Exception("chiave TMDB non valida");
                    if ((code == 429 || code >= 500 || code == 0) && attempt < 2) { Thread.Sleep(2000 * (attempt + 1)); continue; }
                    throw;
                }
            }
        }

        // "" se la chiave funziona, altrimenti il motivo
        public static string TestKey(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) return "chiave vuota";
            try { Get(key, "configuration", ""); return ""; }
            catch (Exception ex) { return ex.Message; }
        }

        static string Str(Dictionary<string, object> m, string k)
        {
            object v;
            return m.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
        }

        static double Num(Dictionary<string, object> m, string k)
        {
            object v;
            if (!m.TryGetValue(k, out v) || v == null) return 0;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        static int YearOf(string date)
        {
            int y;
            return date != null && date.Length >= 4 && int.TryParse(date.Substring(0, 4), out y) ? y : 0;
        }

        // ---- file IMDb

        void UpdateImdb()
        {
            HashSet<string> need = new HashSet<string>();
            DateTime fileTime = File.Exists(imdbFile) ? File.GetLastWriteTime(imdbFile) : DateTime.MinValue;
            bool old = (DateTime.Now - fileTime).TotalDays >= 7;
            lock (sync)
            {
                foreach (Entry e in cache.Values)
                    if (e.Found && e.ImdbId.Length > 0 && (e.ImdbChecked < fileTime || old)) need.Add(e.ImdbId);
            }
            if (need.Count == 0) return;
            if (old)
            {
                Status = "Scaricamento dei voti IMDb...";
                string tmp = imdbFile + ".tmp";
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ImdbUrl);
                    req.UserAgent = "RegistraTV";
                    req.Timeout = 30000;
                    req.ReadWriteTimeout = 120000;
                    using (WebResponse resp = req.GetResponse())
                    using (Stream s = resp.GetResponseStream())
                    using (FileStream f = File.Create(tmp)) { s.CopyTo(f); }
                    if (File.Exists(imdbFile)) File.Delete(imdbFile);
                    File.Move(tmp, imdbFile);
                }
                catch (Exception ex)
                {
                    try { File.Delete(tmp); } catch { }
                    if (!File.Exists(imdbFile)) { Error = "voti IMDb non scaricati: " + ex.Message; return; }
                }
                fileTime = File.GetLastWriteTime(imdbFile);
            }
            Status = "Lettura dei voti IMDb...";
            Dictionary<string, double[]> found = new Dictionary<string, double[]>();
            using (FileStream fs = File.OpenRead(imdbFile))
            using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
            using (StreamReader sr = new StreamReader(gz, Encoding.UTF8))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    int t1 = line.IndexOf('\t');
                    if (t1 <= 0 || !need.Contains(line.Substring(0, t1))) continue;
                    string[] f = line.Split('\t');
                    double v; int n;
                    if (f.Length >= 3 && double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v) && int.TryParse(f[2], out n))
                        found[f[0]] = new double[] { v, n };
                }
            }
            lock (sync)
            {
                foreach (Entry e in cache.Values)
                {
                    if (!e.Found || e.ImdbId.Length == 0 || !need.Contains(e.ImdbId)) continue;
                    double[] r;
                    if (found.TryGetValue(e.ImdbId, out r)) { e.ImdbVote = r[0]; e.ImdbVotes = (int)r[1]; }
                    e.ImdbChecked = fileTime;
                }
            }
        }

        // ---- cache su disco (righe separate da tabulazioni)

        void LoadCache()
        {
            if (!File.Exists(cacheFile)) return;
            foreach (string line in File.ReadAllLines(cacheFile, Encoding.UTF8))
            {
                string[] f = line.Split('\t');
                if (f.Length < 12) continue;
                try
                {
                    Entry e = new Entry();
                    e.Found = f[1] == "1";
                    e.TmdbId = int.Parse(f[2]);
                    e.ImdbId = f[3];
                    e.OriginalTitle = f[4];
                    e.Year = int.Parse(f[5]);
                    e.TmdbVote = double.Parse(f[6], CultureInfo.InvariantCulture);
                    e.TmdbCount = int.Parse(f[7]);
                    e.ImdbVote = double.Parse(f[8], CultureInfo.InvariantCulture);
                    e.ImdbVotes = int.Parse(f[9]);
                    e.Checked = DateTime.ParseExact(f[10], "yyyyMMddHHmm", CultureInfo.InvariantCulture);
                    e.ImdbChecked = DateTime.ParseExact(f[11], "yyyyMMddHHmm", CultureInfo.InvariantCulture);
                    e.Ver = f.Length > 12 ? int.Parse(f[12]) : 1;
                    cache[f[0]] = e;
                }
                catch { }
            }
        }

        void SaveCache()
        {
            List<string> lines = new List<string>();
            lock (sync)
            {
                foreach (KeyValuePair<string, Entry> kv in cache)
                {
                    Entry e = kv.Value;
                    // le voci non trovate da piu' di 60 giorni non servono piu'
                    if (!e.Found && (DateTime.Now - e.Checked).TotalDays > 60) continue;
                    lines.Add(string.Join("\t", new string[] {
                        kv.Key, e.Found ? "1" : "0", e.TmdbId.ToString(CultureInfo.InvariantCulture), e.ImdbId,
                        e.OriginalTitle.Replace('\t', ' '), e.Year.ToString(CultureInfo.InvariantCulture),
                        e.TmdbVote.ToString(CultureInfo.InvariantCulture), e.TmdbCount.ToString(CultureInfo.InvariantCulture),
                        e.ImdbVote.ToString(CultureInfo.InvariantCulture), e.ImdbVotes.ToString(CultureInfo.InvariantCulture),
                        e.Checked.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture),
                        e.ImdbChecked.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture),
                        e.Ver.ToString(CultureInfo.InvariantCulture) }));
                }
            }
            string tmp = cacheFile + ".tmp";
            File.WriteAllLines(tmp, lines.ToArray(), new UTF8Encoding(false));
            if (File.Exists(cacheFile)) File.Delete(cacheFile);
            File.Move(tmp, cacheFile);
        }
    }
}