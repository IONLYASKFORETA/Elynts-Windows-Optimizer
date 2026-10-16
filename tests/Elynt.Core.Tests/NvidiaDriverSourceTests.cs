using System.Net;
using Elynt.Core.Drivers;

namespace Elynt.Core.Tests;

/// <summary>NVIDIA's driver lookup, against a recorded-shape response from its AjaxDriverService endpoint.</summary>
public class NvidiaDriverSourceTests
{
    // Shaped like the live reply recorded in research/driver-install-flow.md: newest first, Game Ready and Studio mixed.
    private const string Reply = """
        {"Success":"1","IDS":[
          {"downloadInfo":{"Version":"617.42","IsCRD":"0","IsBeta":"0","IsWHQL":"1","ReleaseDateTime":"Tue Oct 07, 2026",
            "DownloadURL":"https://us.download.nvidia.com/Windows/617.42/617.42-desktop-win10-win11-64bit-international-dch-whql.exe"}},
          {"downloadInfo":{"Version":"616.10","IsCRD":"1","IsBeta":"0","IsWHQL":"1","ReleaseDateTime":"Tue Sep 30, 2026",
            "DownloadURL":"https://us.download.nvidia.com/Windows/616.10/616.10-desktop-win10-win11-64bit-international-dch-whql.exe"}},
          {"downloadInfo":{"Version":"612.33","IsCRD":"0","IsBeta":"0","IsWHQL":"1","ReleaseDateTime":"Tue Sep 16, 2026",
            "DownloadURL":"https://us.download.nvidia.com/Windows/612.33/612.33-desktop-win10-win11-64bit-international-dch-whql.exe"}}
        ]}
        """;

    private readonly StubHandler _http = new(Reply);

    private NvidiaDriverSource Source() => new(new HttpClient(_http));

    [Fact]
    public void Game_ready_is_the_newest_non_studio_driver()
    {
        var offer = Source().FindLatest(new DriverQuery("NVIDIA GeForce RTX 4070", Laptop: false, DriverBranch.GameReady));

        Assert.NotNull(offer);
        Assert.Equal("617.42", offer.Version);
        Assert.Equal(DriverBranch.GameReady, offer.Branch);
        Assert.Equal("https://us.download.nvidia.com/Windows/617.42/617.42-desktop-win10-win11-64bit-international-dch-whql.exe", offer.DownloadUrl.ToString());
    }

    [Fact]
    public void Studio_is_the_newest_studio_driver()
    {
        var offer = Source().FindLatest(new DriverQuery("NVIDIA GeForce RTX 4070", Laptop: false, DriverBranch.Studio));

        Assert.Equal("616.10", offer?.Version);
        Assert.Equal(DriverBranch.Studio, offer?.Branch);
    }

    [Fact]
    public void The_lookup_asks_for_the_gpus_product_family_on_windows_11()
    {
        Source().FindLatest(new DriverQuery("NVIDIA GeForce RTX 4070", Laptop: false, DriverBranch.GameReady));

        var query = _http.LastRequest!.RequestUri!.Query;
        Assert.Contains("pfid=1015", query);
        Assert.Contains("osID=135", query);
        Assert.Contains("dch=1", query);
    }

    [Fact]
    public void A_laptop_gpu_uses_its_notebook_product_family()
    {
        Source().FindLatest(new DriverQuery("NVIDIA GeForce RTX 4070 Laptop GPU", Laptop: true, DriverBranch.GameReady));

        Assert.Contains("pfid=1006", _http.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public void A_gpu_the_app_doesnt_know_finds_nothing_without_asking_nvidia()
    {
        var offer = Source().FindLatest(new DriverQuery("NVIDIA Imaginary 9999", Laptop: false, DriverBranch.GameReady));

        Assert.Null(offer);
        Assert.Null(_http.LastRequest);
    }

    [Fact]
    public void No_results_finds_nothing()
    {
        _http.Body = """{"Success":"0","IDS":[]}""";

        Assert.Null(Source().FindLatest(new DriverQuery("NVIDIA GeForce RTX 4070", Laptop: false, DriverBranch.GameReady)));
    }

    [Fact]
    public void A_download_link_off_nvidias_servers_is_refused()
    {
        _http.Body = Reply.Replace("https://us.download.nvidia.com/", "https://nvidia.example.com/");

        Assert.ThrowsAny<Exception>(() => Source().FindLatest(new DriverQuery("NVIDIA GeForce RTX 4070", Laptop: false, DriverBranch.GameReady)));
    }

    [Fact]
    public void An_http_error_fails_the_lookup()
    {
        _http.Status = HttpStatusCode.InternalServerError;

        Assert.ThrowsAny<HttpRequestException>(() => Source().FindLatest(new DriverQuery("NVIDIA GeForce RTX 4070", Laptop: false, DriverBranch.GameReady)));
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public string Body { get; set; } = body;
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}
