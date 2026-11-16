
namespace DoveLoFanno.Sources;

/// <summary>URL da dare a ffmpeg dopo i reindirizzamenti, e formato da forzare ("hls", "dash" o "").</summary>
public sealed record ResolvedStream(string Url, string Format);

/// <summary>
/// Portato da Resolve-StreamUrl: segue i reindirizzamenti (relinker Rai) e, se l'indirizzo finale non ha
/// un'estensione da playlist, forza -f hls o -f dash dal Content-Type (ffmpeg 7.1+ rifiuta HLS con estensione
/// e MIME non standard). In caso di errore restituisce l'URL di partenza e lascia decidere a ffmpeg.
/// </summary>
public sealed class StreamResolver(HttpClient http)
{
    public async Task<ResolvedStream> ResolveAsync(string url, string userAgent, string referrer, CancellationToken ct = default)
    {
        string final = url, ctype = "";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (userAgent.Length > 0) req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            if (referrer.Length > 0 && Uri.TryCreate(referrer, UriKind.Absolute, out var r)) req.Headers.Referrer = r;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var uri = resp.RequestMessage?.RequestUri;
            if (uri != null && uri.AbsoluteUri != new Uri(url).AbsoluteUri) final = uri.AbsoluteUri;
            ctype = resp.Content.Headers.ContentType?.MediaType ?? "";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException) { }
        return new ResolvedStream(final, FormatFor(final, ctype));
    }

    /// <summary>"hls" o "dash" se l'URL non ha estensione da playlist e il tipo lo dice, altrimenti "".</summary>
    public static string FormatFor(string url, string contentType)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return "";
        string path = u.AbsolutePath;
        if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase)) return "";
        if (contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)) return "hls";
        if (contentType.Contains("dash+xml", StringComparison.OrdinalIgnoreCase)) return "dash";
        return "";
    }

    /// <summary>HttpClient adatto: segue fino a 10 reindirizzamenti, senza cookie.</summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10,
        UseCookies = false,
        AutomaticDecompression = System.Net.DecompressionMethods.All,
    })
    { Timeout = Timeout.InfiniteTimeSpan };
}
