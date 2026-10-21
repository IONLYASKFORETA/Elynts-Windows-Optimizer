using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Elynt.Core.Drivers;

/// <summary>
/// The latest DDU, read from its winget-pkgs manifest: the download comes from Wagnardsoft's own site, and the manifest
/// gives the SHA-256 that winget's validation pipeline recorded for it. Wagnardsoft's site couldn't be reached while
/// this was built, and DDU isn't known to be Authenticode-signed, so the hash is what the download is checked against.
/// winget can lag Wagnardsoft's site by a release or two.
/// </summary>
public sealed partial class WingetDduSource(HttpClient http) : IDduSource
{
    private const string Folder =
        "https://api.github.com/repos/microsoft/winget-pkgs/contents/manifests/w/Wagnardsoft/DisplayDriverUninstaller";

    private const string RawFolder =
        "https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Wagnardsoft/DisplayDriverUninstaller";

    public string? Signer => null;

    public string ProjectPage => "https://www.wagnardsoft.com/display-driver-uninstaller-DDU-";

    public DduOffer FindLatest()
    {
        using var listing = JsonDocument.Parse(Get(Folder));
        var version = listing.RootElement.EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "dir")
            .Select(e => e.GetProperty("name").GetString())
            .Select(name => Version.TryParse(name, out var v) ? v : null)
            .OfType<Version>()
            .DefaultIfEmpty()
            .Max() ?? throw new InvalidOperationException("winget lists no DDU versions.");

        var manifest = Get($"{RawFolder}/{version}/Wagnardsoft.DisplayDriverUninstaller.installer.yaml");
        var url = InstallerUrl().Match(manifest) is { Success: true } u
            ? new Uri(u.Groups[1].Value)
            : throw new InvalidOperationException("DDU's winget manifest has no installer URL.");
        var sha = InstallerSha256().Match(manifest) is { Success: true } s
            ? s.Groups[1].Value
            : throw new InvalidOperationException("DDU's winget manifest has no installer hash.");

        // Only ever download DDU from Wagnardsoft itself, whatever the manifest says.
        if (url.Scheme != Uri.UriSchemeHttps || !url.Host.Equals("www.wagnardsoft.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"DDU's winget manifest points somewhere other than Wagnardsoft: {url}.");

        return new DduOffer(version.ToString(), url, sha);
    }

    private string Get(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ElyntOptimizer", "1.0"));
        using var response = http.Send(request);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(response.Content.ReadAsStream());
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"InstallerUrl:\s*(\S+)")]
    private static partial Regex InstallerUrl();

    [GeneratedRegex(@"InstallerSha256:\s*([0-9A-Fa-f]{64})")]
    private static partial Regex InstallerSha256();
}
