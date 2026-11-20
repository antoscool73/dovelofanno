using System.Text.Json;
using DoveLoFanno.Ratings;

namespace DoveLoFanno.Data;

/// <summary>
/// La ricerca avanzata salvata in una lista dinamica (19/11/2026): gli stessi campi dei filtri della pagina Cerca.
/// Q = titolo, regista o attore; Kind = all, film, series, documentary; Genres = codici TMDB (uno qualsiasi);
/// Avail = tv (in TV gratis), paytv (in TV a pagamento), mine, paid, home (uno qualsiasi); HideSeen e HideHome tolgono i
/// già visti e quelli in casa.
/// </summary>
public sealed record SavedQuery(string Q = "", string Kind = "all", IReadOnlyList<int>? Genres = null, int YearFrom = 0, int YearTo = 0,
    string Country = "", double MinRating = 0, IReadOnlyList<string>? Avail = null, bool HideSeen = false, bool HideHome = false)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static readonly string[] Kinds = ["all", "film", "series", "documentary"];
    static readonly string[] Avails = ["tv", "paytv", "mine", "paid", "home"];

    public SearchFilter ToFilter() => new(Kinds.Contains(Kind) ? Kind : "all", Genres ?? [], YearFrom, YearTo, (Country ?? "").Trim().ToUpperInvariant(),
        MinRating, (Avail ?? []).Where(Avails.Contains).ToHashSet(), HideSeen, HideHome);

    /// <summary>Messaggio d'errore in italiano, o vuoto se va bene. Serve almeno un testo o un filtro.</summary>
    public string Validate()
    {
        string q = (Q ?? "").Trim();
        if (q.Length > 100) return "testo della ricerca più lungo di 100 caratteri";
        if (!Kinds.Contains(Kind)) return "tipo: all, film, series o documentary";
        if ((Avail ?? []).Any(a => !Avails.Contains(a))) return "disponibilità: tv, paytv, mine, paid o home";
        if ((Genres ?? []).Count > 20) return "troppi generi";
        if (MinRating is < 0 or > 10) return "voto minimo da 0 a 10";
        if (YearFrom is < 0 or > 2100 || YearTo is < 0 or > 2100) return "anno non valido";
        if (q.Length < 2 && !ToFilter().Any) return "scegli almeno un filtro o scrivi almeno 2 lettere";
        return "";
    }

    public string ToJson() => JsonSerializer.Serialize(this with { Q = (Q ?? "").Trim() }, Json);

    /// <summary>null se il testo è vuoto o non si legge (lista non dinamica).</summary>
    public static SavedQuery? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<SavedQuery>(json, Json); } catch (JsonException) { return null; }
    }
}
