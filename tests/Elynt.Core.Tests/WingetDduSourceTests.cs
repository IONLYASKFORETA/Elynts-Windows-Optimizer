using System.Net;
using Elynt.Core.Drivers;

namespace Elynt.Core.Tests;

/// <summary>Finding the latest DDU from its winget-pkgs manifest, against canned GitHub replies.</summary>
public class WingetDduSourceTests
{
    private const string Folder =
        "https://api.github.com/repos/microsoft/winget-pkgs/contents/manifests/w/Wagnardsoft/DisplayDriverUninstaller";

    // Shaped like the GitHub contents API: one entry per version folder, in name order (so 18.1.10.0 sorts before 18.1.5.3).
    private const string Listing = """
        [{"name":"18.0.8.0","type":"dir"},{"name":"18.1.10.0","type":"dir"},{"name":"18.1.5.3","type":"dir"},{"name":"README.md","type":"file"}]
        """;

    // Shaped like the real 18.1.5.3 installer manifest (research checked it on 2026-10-18).
    private const string Manifest = """
        PackageIdentifier: Wagnardsoft.DisplayDriverUninstaller
        PackageVersion: 18.1.10.0
        InstallerType: nullsoft
        Installers:
        - Architecture: x64
          InstallerUrl: https://www.wagnardsoft.com/DDU/download/DDU%20v18.1.10.0_setup.exe
          InstallerSha256: 1E854C00AA7086A85559551ABBFB8F0F3EBBB88A1C6126C6E7A1D0A61F25C0A1
        ManifestType: installer
        """;

    private sealed class Routes(Dictionary<string, string> replies) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return replies.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static readonly Dictionary<string, string> Replies = new()
    {
        [Folder] = Listing,
        ["https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/w/Wagnardsoft/DisplayDriverUninstaller/18.1.10.0/Wagnardsoft.DisplayDriverUninstaller.installer.yaml"] = Manifest,
    };

    [Fact]
    public void The_newest_version_is_offered_with_its_wagnardsoft_url_and_hash()
    {
        var routes = new Routes(Replies);

        var offer = new WingetDduSource(new HttpClient(routes)).FindLatest();

        Assert.Equal("18.1.10.0", offer.Version);
        Assert.Equal("https://www.wagnardsoft.com/DDU/download/DDU%20v18.1.10.0_setup.exe", offer.DownloadUrl.AbsoluteUri);
        Assert.Equal("1E854C00AA7086A85559551ABBFB8F0F3EBBB88A1C6126C6E7A1D0A61F25C0A1", offer.Sha256);
        Assert.All(routes.Requests, r => Assert.True(r.Headers.UserAgent.Count > 0, "GitHub's API needs a User-Agent"));
    }

    [Fact]
    public void A_manifest_pointing_anywhere_but_wagnardsoft_is_refused()
    {
        var replies = new Dictionary<string, string>(Replies);
        var manifestUrl = replies.Keys.Single(k => k.StartsWith("https://raw.", StringComparison.Ordinal));
        replies[manifestUrl] = Manifest.Replace("https://www.wagnardsoft.com/", "https://example.com/");

        Assert.ThrowsAny<Exception>(() => new WingetDduSource(new HttpClient(new Routes(replies))).FindLatest());
    }

    [Fact]
    public void A_failed_listing_throws()
    {
        Assert.ThrowsAny<Exception>(() => new WingetDduSource(new HttpClient(new Routes([]))).FindLatest());
    }

    [Fact]
    public void It_relies_on_the_published_hash_not_a_signer()
    {
        var source = new WingetDduSource(new HttpClient(new Routes(Replies)));

        Assert.Null(source.Signer);
        Assert.StartsWith("https://www.wagnardsoft.com/", source.ProjectPage);
    }
}
