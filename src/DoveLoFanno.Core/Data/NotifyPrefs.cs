namespace DoveLoFanno.Data;

/// <summary>
/// Un tipo di notifica e dove arriva per un profilo: Web = campanella dell'app (e notifica del browser, che la segue);
/// Telegram = la chat della famiglia. WebLocked: nella campanella c'è sempre (avvisi creati a mano, problemi delle
/// registrazioni), si sceglie solo Telegram.
/// </summary>
public sealed record NotifyPref(string Kind, bool Web, bool Telegram, bool WebLocked);

/// <summary>
/// Preferenze delle notifiche per tipo e canale di notifica, per profilo (26/11/2026, richiesta di Antonio). Si salvano
/// in settings come NotifyOff.{profilo} = elenco di «tipo.web» e «tipo.telegram» spenti: senza nulla salvato arriva tutto,
/// come prima. I tipi che non sono nell'elenco (notifiche vecchie) passano sempre.
/// </summary>
public static class NotifyPrefs
{
    /// <summary>
    /// recording = registrazioni finite; recording_problem = incomplete o non riuscite; series = puntate di una serie
    /// programmate; alert = avvisi scattati; proposals = proposte nuove; library = pronti da guardare e conversioni;
    /// system = aggiornamenti (solo adulti).
    /// </summary>
    public static readonly string[] Kinds = ["recording", "recording_problem", "series", "alert", "proposals", "library", "system"];

    static readonly HashSet<string> WebLockedKinds = ["recording_problem", "alert"];

    static HashSet<string> Off(SettingsStore s, long profileId) =>
        [.. s.Get($"NotifyOff.{profileId}").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public static List<NotifyPref> Load(SettingsStore s, long profileId)
    {
        var off = Off(s, profileId);
        return [.. Kinds.Select(k => new NotifyPref(k, WebLockedKinds.Contains(k) || !off.Contains(k + ".web"), !off.Contains(k + ".telegram"), WebLockedKinds.Contains(k)))];
    }

    /// <summary>Salva le scelte; i tipi sconosciuti si ignorano, quelli bloccati restano nella campanella.</summary>
    public static List<NotifyPref> Save(SettingsStore s, long profileId, IEnumerable<NotifyPref> prefs)
    {
        var off = new List<string>();
        foreach (var p in prefs.Where(p => Kinds.Contains(p.Kind)).DistinctBy(p => p.Kind))
        {
            if (!p.Web && !WebLockedKinds.Contains(p.Kind)) off.Add(p.Kind + ".web");
            if (!p.Telegram) off.Add(p.Kind + ".telegram");
        }
        s.Set($"NotifyOff.{profileId}", string.Join(",", off));
        return Load(s, profileId);
    }

    /// <summary>Tipi che il profilo non vuole nella campanella.</summary>
    public static IReadOnlySet<string> HiddenInApp(SettingsStore s, long profileId) =>
        Off(s, profileId).Where(x => x.EndsWith(".web", StringComparison.Ordinal)).Select(x => x[..^4]).Where(k => !WebLockedKinds.Contains(k)).ToHashSet();

    /// <summary>Il profilo vuole questo tipo su Telegram.</summary>
    public static bool Telegram(SettingsStore s, long profileId, string kind) => !Off(s, profileId).Contains(kind + ".telegram");
}
