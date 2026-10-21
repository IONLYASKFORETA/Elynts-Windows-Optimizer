namespace Elynt.Core.Drivers;

/// <summary>Downloads over HTTPS only, writing to a temporary name first so a cut-off download never looks complete.</summary>
public sealed class HttpDownloader(HttpClient http) : IDownloader
{
    public void Download(Uri url, string path, Uri? referer = null)
    {
        if (url.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"Only HTTPS downloads are allowed: {url}.");

        var partial = path + ".partial";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer = referer;
            // Vendor servers (AMD's) refuse requests that don't look like a browser.
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            using (var response = http.Send(request, HttpCompletionOption.ResponseHeadersRead))
            {
                // AMD answers a refused download with a redirect to an HTML "Download-Incomplete" page, not an error.
                if (response.Content.Headers.ContentType?.MediaType is "text/html")
                    throw new InvalidDataException($"The server sent a web page instead of the driver ({response.RequestMessage?.RequestUri}).");
                response.EnsureSuccessStatusCode();
                using var body = response.Content.ReadAsStream();
                using var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None);
                body.CopyTo(file);
            }

            File.Move(partial, path, overwrite: true);
        }
        finally
        {
            File.Delete(partial);
        }
    }
}
