using System.Globalization;

namespace DoveLoFanno.Epg;

/// <summary>
/// Un programma della guida XMLTV. Portato da RegistraTV.EpgProgramme (RegistraTV.ps1, versione registratv-v1.0).
/// Gli orari sono locali, come nella versione PowerShell, per avere gli stessi WorkKey.
/// </summary>
public sealed class EpgProgramme
{
    public string Channel { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime Stop { get; set; }
    public string Title { get; set; } = "";
    public string SubTitle { get; set; } = "";
    public string Desc { get; set; } = "";
    public string Category { get; set; } = "";
    public string[] Categories { get; set; } = [];
    public int Year { get; set; }
    public string Country { get; set; } = "";
    public string Directors { get; set; } = "";
    public string Actors { get; set; } = "";
    public string Episode { get; set; } = "";
    /// <summary>Immagine del programma indicata dalla guida (icon src), se c'è.</summary>
    public string Icon { get; set; } = "";

    /// <summary>Gruppo fisso: Film, Serie TV, Documentari, Notizie, Sport, Intrattenimento, Bambini, Altro.</summary>
    public string Group { get; set; } = Genres.Other;

    /// <summary>Sottogenere (per i film: Commedia, Drammatico...).</summary>
    public string Sub { get; set; } = "";

    /// <summary>Vero se il gruppo è dedotto da titolo e canale perché il programma non ha categorie utili.</summary>
    public bool GroupGuessed { get; set; }

    public string Genre => Sub.Length > 0 ? Group + " · " + Sub : Group;

    public string TitleKey => Titles.TitleKey(Title);

    /// <summary>
    /// Stessa opera: i film per titolo e anno, il resto per titolo ed episodio (o sottotitolo);
    /// senza episodio ogni passaggio è a sé (non si possono riconoscere le repliche).
    /// </summary>
    public string WorkKey
    {
        get
        {
            string t = Titles.TitleKey(Title);
            if (Group == Genres.Film) return t + "|" + Year.ToString(CultureInfo.InvariantCulture);
            string ep = Episode.Length > 0 ? Titles.Plain(Episode) : Titles.TitleKey(SubTitle);
            if (ep.Length == 0) ep = "@" + Start.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
            return t + "|" + ep;
        }
    }
}
