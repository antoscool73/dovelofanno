using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DoveLoFanno.Epg;

/// <summary>Normalizzazione di titoli e nomi di canale (da Genres.Plain, EpgGuide.TitleKey e EpgGuide.Norm).</summary>
public static partial class Titles
{
    /// <summary>Minuscole senza accenti; l'apostrofo separa le parole ("L' Amica" e "L'amica" diventano "l amica").</summary>
    public static string Plain(string? s)
    {
        if (s == null) return "";
        string d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var b = new StringBuilder(d.Length);
        foreach (char c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) b.Append(c);
        return b.ToString().Replace('\'', ' ').Replace('’', ' ');
    }

    /// <summary>Titolo per i confronti: minuscole, senza accenti, punteggiatura e articolo iniziale.</summary>
    public static string TitleKey(string? s)
    {
        string t = Plain(s);
        t = NonAlnum().Replace(t, " ").Trim();
        t = LeadingArticle().Replace(t, "");
        return t;
    }

    /// <summary>Nome di canale normalizzato: "Canale.5.it", "Rai 1 HD  101", "TV8.HD.it" diventano "canale5", "rai1", "tv8".</summary>
    public static string ChannelKey(string? s)
    {
        if (s == null) return "";
        s = s.ToLowerInvariant().Trim();
        s = ItSuffix().Replace(s, "");
        s = TrailingNumbering().Replace(s, "");
        s = HdWord().Replace(s, "");
        return NonAlnumSingle().Replace(s, "");
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlnumSingle();

    [GeneratedRegex(@"^(il|lo|la|i|gli|le|l|un|uno|una|the|a|an)\s+")]
    private static partial Regex LeadingArticle();

    [GeneratedRegex(@"\.it$")]
    private static partial Regex ItSuffix();

    [GeneratedRegex(@"[.\s]{2,}\d+$")]
    private static partial Regex TrailingNumbering();

    [GeneratedRegex(@"\b(hd|fhd|uhd)\b")]
    private static partial Regex HdWord();
}
