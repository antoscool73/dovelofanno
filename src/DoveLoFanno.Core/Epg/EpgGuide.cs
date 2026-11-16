using System.Globalization;

namespace DoveLoFanno.Epg;

/// <summary>
/// Guida caricata in memoria: abbinamento dei canali della playlist e ricerche.
/// Portata dalla parte di interrogazione di RegistraTV.EpgGuide; download e copia locale stanno altrove.
/// </summary>
public sealed class EpgGuide
{
    // nomi della playlist che la normalizzazione non basta ad abbinare
    static readonly Dictionary<string, string> Alias = new()
    {
        ["mediaset20"] = "20", ["cielotv"] = "cielo", ["mediaset27twentyseven"] = "27twentyseven",
        ["tgcom24"] = "tgcom", ["rtl1025tv"] = "rtl1025", ["radio105tv"] = "radio105",
        ["radiomontecarlo"] = "rmc", ["radiofrecciatv"] = "radiofreccia", ["discovery"] = "discoverychannel",
    };

    readonly Dictionary<string, List<EpgProgramme>> byId;
    readonly Dictionary<string, string> byKey;

    readonly Dictionary<string, string> names;

    internal EpgGuide(Dictionary<string, List<EpgProgramme>> byId, Dictionary<string, string> byKey, DateTime lastStop,
        Dictionary<string, string>? names = null)
    {
        this.names = names ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        this.byId = byId;
        this.byKey = byKey;
        LastStop = lastStop;
        ProgrammeCount = byId.Values.Sum(l => l.Count);
    }

    public DateTime LastStop { get; }
    public int ChannelCount => byId.Count;
    public int ProgrammeCount { get; }
    public IEnumerable<string> ChannelIds => byId.Keys;

    /// <summary>Nome del canale come lo scrive la guida (primo display-name), altrimenti l'id.</summary>
    public string ChannelName(string id) => names.TryGetValue(id, out var n) ? n : id;

    /// <summary>Voto di un film (0 = nessuno); facoltativo, lo collega il servizio dei voti.</summary>
    public Func<EpgProgramme, double>? Ratings { get; set; }

    public IReadOnlyList<EpgProgramme> Programmes(string id) =>
        byId.TryGetValue(id, out var l) ? l : [];

    /// <summary>Canale della guida per un canale della playlist: tvg-id esatto, poi nome normalizzato, poi alias.</summary>
    public string? Resolve(string? tvgId, string? name)
    {
        if (!string.IsNullOrEmpty(tvgId) && byId.ContainsKey(tvgId)) return tvgId;
        foreach (string? s in new[] { tvgId, name })
        {
            string k = Titles.ChannelKey(s);
            if (k.Length == 0) continue;
            if (byKey.TryGetValue(k, out var id)) return id;
            if (Alias.TryGetValue(k, out var a) && byKey.TryGetValue(a, out id)) return id;
        }
        return null;
    }

    /// <summary>Programmi del canale che si sovrappongono a [from, to), filtrati per testo nel titolo o sottotitolo.</summary>
    public List<EpgProgramme> Find(string? id, string? text, DateTime from, DateTime to)
    {
        var res = new List<EpgProgramme>();
        if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out var list)) return res;
        foreach (var p in list)
        {
            if (p.Stop <= from || p.Start >= to) continue;
            if (!string.IsNullOrEmpty(text) && !Has(p.Title, text) && !Has(p.SubTitle, text)) continue;
            res.Add(p);
        }
        return res;
    }

    public double RatingOf(EpgProgramme p)
    {
        var r = Ratings;
        if (r == null || p.Group != Genres.Film) return 0;
        return r(p);
    }

    /// <summary>
    /// Ricerca su più canali: testo nel titolo, sottotitolo, regista o attori; gruppo e sottogenere (vuoti = tutti);
    /// voto minimo (0 = nessun filtro). Ordinata per orario e canale, al massimo <paramref name="max"/> risultati.
    /// </summary>
    public List<EpgProgramme> Search(IEnumerable<string> ids, string? text, string? group, string? sub, double minRating, DateTime from, DateTime to, int max)
    {
        var res = new List<EpgProgramme>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in ids)
        {
            if (string.IsNullOrEmpty(id) || !seen.Add(id) || !byId.TryGetValue(id, out var list)) continue;
            foreach (var p in list)
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

    /// <summary>Sottogeneri dei film presenti almeno <paramref name="minCount"/> volte, in ordine alfabetico.</summary>
    public List<string> FilmSubs(int minCount)
    {
        var n = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in byId.Values)
            foreach (var p in l)
                if (p.Group == Genres.Film && p.Sub.Length > 0) n[p.Sub] = n.GetValueOrDefault(p.Sub) + 1;
        var res = n.Where(kv => kv.Value >= minCount).Select(kv => kv.Key).ToList();
        res.Sort(StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), ignoreCase: true));
        return res;
    }

    /// <summary>Film non ancora finiti dei canali indicati, da passare al servizio dei voti.</summary>
    public List<EpgProgramme> Films(IEnumerable<string> ids, DateTime now)
    {
        var res = new List<EpgProgramme>();
        foreach (string id in ids)
        {
            if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out var list)) continue;
            res.AddRange(list.Where(p => p.Group == Genres.Film && p.Stop > now));
        }
        return res;
    }

    static bool Has(string? s, string t) =>
        s != null && CultureInfo.InvariantCulture.CompareInfo.IndexOf(s, t, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
}
