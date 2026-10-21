using Elynt.Core.Drivers;

namespace Elynt.Core.Tests;

/// <summary>AMD's Adrenalin and chipset lookups, against pages shaped like the live ones in research/driver-install-flow.md.</summary>
public class AmdDriverSourceTests
{
    private const string DriversPage = "https://www.amd.com/en/support/download/drivers.html";

    // The drivers page links the minimal-setup (auto-detect) installer; its file name carries the current version.
    private const string Page = """
        <html><body><a class="btn" href="https://drivers.amd.com/drivers/installer/26.10/whql/amd-software-adrenalin-edition-26.8.1-minimalsetup-260818_web.exe">Download</a>
        <a href="https://drivers.amd.com/relnotes/command-line-installation.pdf">Command line</a></body></html>
        """;

    private readonly StubHttpHandler _http = new() { Bodies = { [DriversPage] = Page } };

    private AmdGpuDriverSource Gpu() => new(new HttpClient(_http));

    private static DriverQuery Radeon(string name = "AMD Radeon RX 7800 XT") => new(name, Laptop: false, DriverBranch.GameReady);

    [Fact]
    public void The_latest_adrenalin_driver_is_the_combined_package_for_the_version_on_amds_page()
    {
        var offer = Gpu().FindLatest(Radeon());

        Assert.NotNull(offer);
        Assert.Equal("26.8.1", offer.Version);
        Assert.Equal("https://drivers.amd.com/drivers/whql-amd-software-adrenalin-edition-26.8.1-win11-c.exe", offer.DownloadUrl.ToString());
        Assert.Equal(DriversPage, offer.Referer?.ToString());
    }

    [Theory]
    [InlineData("Radeon RX 580 Series")]
    [InlineData("AMD Radeon RX 480")]
    [InlineData("Radeon RX Vega")]
    [InlineData("AMD Radeon VII")]
    [InlineData("AMD Radeon R9 200 Series")]
    [InlineData("AMD Radeon HD 7900 Series")]
    [InlineData("AMD Radeon PRO W7900")]
    public void A_gpu_the_combined_package_doesnt_cover_finds_nothing_without_asking_amd(string name)
    {
        Assert.Null(Gpu().FindLatest(Radeon(name)));
        Assert.Empty(_http.Requests);
    }

    [Theory]
    [InlineData("AMD Radeon RX 9070 XT")]
    [InlineData("AMD Radeon RX 5700 XT")]
    [InlineData("AMD Radeon(TM) Graphics")]
    [InlineData("AMD Radeon 780M Graphics")]
    public void Current_radeons_and_ryzen_graphics_are_looked_up(string name) =>
        Assert.Equal("26.8.1", Gpu().FindLatest(Radeon(name))?.Version);

    [Fact]
    public void A_page_without_a_recognisable_installer_fails_the_lookup_instead_of_guessing()
    {
        _http.Bodies[DriversPage] = "<html><body>Our drivers page moved.</body></html>";

        Assert.ThrowsAny<Exception>(() => Gpu().FindLatest(Radeon()));
    }

    [Fact]
    public void A_page_linking_two_different_versions_fails_the_lookup_instead_of_guessing()
    {
        _http.Bodies[DriversPage] = Page + """
            <a href="https://drivers.amd.com/drivers/installer/26.10/beta/amd-software-adrenalin-edition-26.10.1-minimalsetup-261001_web.exe">Preview</a>
            """;

        Assert.ThrowsAny<Exception>(() => Gpu().FindLatest(Radeon()));
    }

    private const string ChipsetFeed = "https://drivers.amd.com/drivers/installer/chipset/version.txt";

    private AmdChipsetDriverSource Chipset() => new(new HttpClient(_http));

    private static DriverQuery Ryzen => new("AMD chipset", Laptop: false, DriverBranch.GameReady);

    [Fact]
    public void The_latest_chipset_driver_is_the_version_in_amds_feed()
    {
        _http.Bodies[ChipsetFeed] = "8.08.12.551\r\n";

        var offer = Chipset().FindLatest(Ryzen);

        Assert.Equal("8.08.12.551", offer?.Version);
        Assert.Equal("https://drivers.amd.com/drivers/AMD_Chipset_Software_8.08.12.551.exe", offer?.DownloadUrl.ToString());
        Assert.Equal(DriversPage, offer?.Referer?.ToString());
        Assert.Equal(DriversPage, _http.Requests.Single().Headers.Referrer?.ToString());
    }

    [Fact]
    public void A_chipset_feed_without_a_version_fails_the_lookup_instead_of_guessing()
    {
        _http.Bodies[ChipsetFeed] = "<html>Download-Incomplete</html>";

        Assert.ThrowsAny<Exception>(() => Chipset().FindLatest(Ryzen));
    }

    [Fact]
    public void The_chipset_installer_runs_silently_and_is_signed_by_amd()
    {
        var installer = Chipset().Installer;

        Assert.Equal(["/S"], installer.Arguments);
        Assert.Equal("Advanced Micro Devices, Inc.", installer.Signer);
        Assert.Equal(DriverKind.Chipset, Chipset().Kind);
    }

    [Fact]
    public void An_http_error_fails_the_lookup()
    {
        _http.Status = System.Net.HttpStatusCode.Forbidden;

        Assert.ThrowsAny<HttpRequestException>(() => Gpu().FindLatest(Radeon()));
    }
}
