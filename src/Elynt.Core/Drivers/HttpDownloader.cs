namespace Elynt.Core.Drivers;

/// <summary>Downloads over HTTPS only, writing to a temporary name first so a cut-off download never looks complete.</summary>
public sealed class HttpDownloader(HttpClient http) : IDownloader
{
    public void Download(Uri url, string path)
    {
        if (url.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"Only HTTPS downloads are allowed: {url}.");

        var partial = path + ".partial";
        try
        {
            using (var response = http.Send(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead))
            {
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
