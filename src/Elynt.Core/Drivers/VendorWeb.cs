namespace Elynt.Core.Drivers;

/// <summary>Fetches a vendor's web page the way a browser would. The vendors' pages refuse plain HTTP clients.</summary>
internal static class VendorWeb
{
    private const string BrowserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

    /// <summary>The page's text. Throws when the request fails.</summary>
    public static string Fetch(HttpClient http, string url, string? referer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", BrowserAgent);
        if (referer is not null)
            request.Headers.Referrer = new Uri(referer);
        using var response = http.Send(request);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(response.Content.ReadAsStream());
        return reader.ReadToEnd();
    }
}
