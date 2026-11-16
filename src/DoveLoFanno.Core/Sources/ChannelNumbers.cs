using DoveLoFanno.Epg;

namespace DoveLoFanno.Sources;

/// <summary>
/// Numero del canale come sul digitale terrestre (LCN nazionale): un'etichetta accanto al nome. L'ordine degli elenchi
/// è quello della playlist (<see cref="Data.SourceStore.Channels"/>); il numero ordina solo i canali che nessuna
/// playlist attiva ha (quelli della sola guida).
/// Fonte: lista LCN nazionale riportata da Punto Informatico il 30/09/2026 ("Nuova lista di canali per il digitale
/// terrestre tra settembre e ottobre"); i canali senza numero nazionale (locali, FAST, radio non in elenco) vanno in
/// fondo in ordine di nome. Il canale si riconosce dal tvg-id o dal nome, normalizzati con <see cref="Titles.ChannelKey"/>.
/// </summary>
public static class ChannelNumbers
{
    static readonly Dictionary<string, int> Table = Build(
        (1, ["rai1"]), (2, ["rai2"]), (3, ["rai3", "rai3tgr"]), (4, ["rete4"]), (5, ["canale5"]), (6, ["italia1"]),
        (7, ["la7"]), (8, ["tv8"]), (9, ["nove"]), (20, ["mediaset20", "20mediaset"]),
        (21, ["rai4"]), (22, ["iris"]), (23, ["rai5"]), (24, ["raimovie"]), (25, ["raipremium"]), (26, ["cielo", "cielotv"]),
        (27, ["twentyseven", "27twentyseven", "mediaset27twentyseven"]), (28, ["tv2000"]), (29, ["la7cinema", "la7d"]),
        (30, ["la5"]), (31, ["realtime"]), (33, ["foodnetwork"]), (34, ["cine34"]), (35, ["focus"]), (36, ["rtl1025", "rtl1025tv"]),
        (37, ["discovery", "discoverytv", "discoverychannel"]), (38, ["giallo", "giallotv"]), (39, ["topcrime"]), (40, ["boing"]),
        (42, ["raigulp"]), (43, ["raiyoyo"]), (46, ["cartoonito"]), (47, ["super"]), (48, ["rainews24", "rainews"]),
        (49, ["italia2"]), (50, ["skytg24"]), (51, ["tgcom24"]), (52, ["dmax"]), (54, ["raistoria"]), (55, ["mediasetextra"]),
        (56, ["hgtv", "hgtvhomegarden"]), (57, ["raiscuola"]), (58, ["raisport", "raisportplus"]), (59, ["discoveryturbo", "motortrend"]),
        (60, ["sportitalia"]), (62, ["donnatv", "donnasporttv"]), (64, ["supertennis"]), (65, ["almatv"]),
        (66, ["radio105tv", "radio105"]), (67, ["r101tv", "r101"]), (69, ["deejaytv"]), (70, ["radioitaliatv"]),
        (158, ["radiokisskiss", "radiokisskisstv"]), (202, ["rairadio2visual", "rairadio2"]), (236, ["manga"]),
        (258, ["radiofreccia", "radiofrecciatv"]), (265, ["rdssocialtv", "rds"]), (266, ["radiozeta"]),
        (510, ["automoto", "automototv"]), (713, ["radiocapitaltv", "radiocapital"]), (715, ["m2otv", "m2o"]),
        (772, ["radiomontecarlo", "rmc"]), (786, ["virginradio", "virginradiotv"]), (833, ["supersix"]));

    static Dictionary<string, int> Build(params (int Number, string[] Keys)[] rows)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (n, keys) in rows)
            foreach (string k in keys) d.Add(k, n);
        return d;
    }

    /// <summary>Numero LCN dal tvg-id (o dalla chiave "id:..."/"name:..." del canale) o dal nome; null se non è in elenco.</summary>
    public static int? For(string? idOrKey, string? name)
    {
        string id = idOrKey ?? "";
        if (id.StartsWith("id:", StringComparison.Ordinal) || id.StartsWith("name:", StringComparison.Ordinal)) id = id[(id.IndexOf(':') + 1)..];
        if (Table.TryGetValue(Titles.ChannelKey(id), out int n)) return n;
        return Table.TryGetValue(Titles.ChannelKey(name), out n) ? n : null;
    }

    /// <summary>Ordine dei canali: prima per numero, poi quelli senza numero per nome.</summary>
    public static int Compare(int? numberA, string nameA, int? numberB, string nameB)
    {
        if (numberA != numberB)
        {
            if (numberA == null) return 1;
            if (numberB == null) return -1;
            return numberA.Value.CompareTo(numberB.Value);
        }
        return string.Compare(nameA, nameB, StringComparison.CurrentCultureIgnoreCase);
    }
}
