using System.Globalization;
using DoveLoFanno.Media;

namespace DoveLoFanno.Recording;

/// <summary>Un salto nel flusso dentro un file: dopo <see cref="At"/> secondi dall'inizio mancano <see cref="Seconds"/> secondi.</summary>
public sealed record StreamGap(string Stream, double At, double Seconds);

/// <summary>
/// Salti dentro uno spezzone (26/11/2026, richiesta di Antonio): segmenti persi mentre ffmpeg continua a scrivere, che il
/// controllo «file fermo» non vede perché il file cresce. Casi veri sull'UN62: «Dream Scenario» (Rai 4, 25/11) con 4
/// salti di 7-10 s nel video, «Amabili resti» (LA7 Cinema) con 18 buchi audio da 4 s. ffprobe legge i tempi di
/// decodifica (dts) dei pacchetti della prima traccia video e della prima audio: tra due pacchetti di fila non passano
/// mai più di qualche decimo di secondo, un salto oltre <c>minGap</c> è contenuto mancante.
/// </summary>
public static class GapScanner
{
    public const double DefaultMinGap = 2.0;

    public static async Task<IReadOnlyList<StreamGap>> ScanAsync(IMediaTools tools, string file, CancellationToken ct, double minGap = DefaultMinGap)
    {
        var found = new List<(string Stream, double From, double Seconds)>();
        double? first = null;
        foreach (var (sel, name) in new[] { ("v:0", "video"), ("a:0", "audio") })
        {
            var (code, stdout, _) = await tools.RunFfprobeAsync(
                ["-v", "error", "-select_streams", sel, "-show_entries", "packet=dts_time", "-of", "csv=p=0", file], TimeSpan.FromMinutes(10), ct);
            if (code != 0) continue;
            double? prev = null;
            foreach (var line in stdout.Split('\n'))
            {
                var t = line.Trim().TrimEnd(',');
                if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) continue;   // N/A
                if (first is not double f || v < f) first = v;
                if (prev is double p)
                {
                    if (v - p > minGap) found.Add((name, p, v - p));
                    if (v <= p) continue;      // pacchetto fuori ordine: si tiene il più avanti
                }
                prev = v;
            }
        }
        double origin = first ?? 0;
        return [.. found.OrderBy(g => g.From).Select(g => new StreamGap(g.Stream, Math.Round(g.From - origin, 2), Math.Round(g.Seconds, 2)))];
    }
}
