namespace DoveLoFanno.Sources;

/// <summary>Un flusso candidato per un canale, con i dati della sua sorgente.</summary>
public sealed record StreamCandidate(
    long StreamId, long SourceId, string Url, string UserAgent, string Referrer,
    string State, int StreamPriority, int SourcePriority, int? MaxConnections);

/// <summary>
/// Ordine in cui provare i flussi di un canale: priorità della sorgente e del flusso (scelte dall'utente),
/// poi stato dell'ultimo controllo. Esclusi i DRM e le sorgenti che hanno già tutte le connessioni occupate.
/// </summary>
public static class StreamSelector
{
    static int StateRank(string state) => state switch
    {
        StreamState.Ok => 0,
        StreamState.Unchecked => 1,
        StreamState.Limit => 2,      // può liberarsi
        StreamState.Unreachable => 3, // può essere tornato
        StreamState.Geo => 4,
        _ => 9,
    };

    /// <param name="inUse">connessioni già aperte per sorgente (registrazioni in corso)</param>
    public static List<StreamCandidate> Order(IEnumerable<StreamCandidate> candidates, IReadOnlyDictionary<long, int> inUse) =>
        candidates
            .Where(c => c.State != StreamState.Drm)
            .Where(c => c.MaxConnections is not int max || inUse.GetValueOrDefault(c.SourceId) < max)
            // i flussi che all'ultimo controllo non andavano vengono dopo tutti gli altri, qualunque sia la priorità
            .OrderBy(c => StateRank(c.State) >= 3 ? 1 : 0)
            .ThenBy(c => c.SourcePriority)
            .ThenBy(c => c.StreamPriority)
            .ThenBy(c => StateRank(c.State))
            .ThenBy(c => c.StreamId)
            .ToList();
}
