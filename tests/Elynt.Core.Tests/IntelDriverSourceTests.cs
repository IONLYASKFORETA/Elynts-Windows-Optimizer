using Elynt.Core.Drivers;

namespace Elynt.Core.Tests;

/// <summary>Intel's graphics and chipset lookups, against pages shaped like Intel's download pages on 2026-10-16.</summary>
public class IntelDriverSourceTests
{
    private const string GraphicsPage = "https://www.intel.com/content/www/us/en/download/785597/intel-arc-graphics-windows.html";
    private const string ChipsetPage = "https://www.intel.com/content/www/us/en/download/19347/chipset-inf-utility.html";
    private const string GraphicsSha512 = "3fffb91635055fadb4ad77afabd8d8801f71b805aa6ffd8d1ec60f1197b68e81c3157eccac9f9f8031a3a996105ac0657c2fa81c076ba7c567ad2ebdbb4d62c9";
    private const string ChipsetSha256 = "F20B9F97E9A81E613D4D529AAD5D3948F5E449998CD118C6DEF4037CA6536446";

    private const string Graphics = $"""
        <html><head>
        <meta name="DownloadVersion" content="32.0.101.9150 WHQL Certified">
        <meta name="RecommendedDownloadUrl" content="https://downloadmirror.intel.com/930514/gfx_win_101.9150.exe">
        <meta name="LastUpdate" content="10/07/2026">
        </head><body>
        <h2>Available Downloads</h2>
        <div>gfx_win_101.9150.exe</div><span>Recommended</span>
        <ul><li>Windows 11 Family*, Windows 10* (22H2)</li><li>Size: 862.6 MB</li><li>SHA512: {GraphicsSha512}</li></ul>
        </body></html>
        """;

    private const string Chipset = $"""
        <html><head><meta name="DownloadVersion" content="10.1.20658.8883"></head><body>
        <h2>Available Downloads</h2>
        <button data-href="https://downloadmirror.intel.com/925343/SetupChipset.exe">Download SetupChipset.exe</button>
        <ul><li>Size: 98.1 MB</li><li>SHA256: {ChipsetSha256}</li></ul>
        <a href="https://downloadmirror.intel.com/925343/Release_notes_10.1.20658.8883-Public-Client.pdf">Release notes</a>
        </body></html>
        """;

    private readonly StubHttpHandler _http = new() { Bodies = { [GraphicsPage] = Graphics, [ChipsetPage] = Chipset } };

    private IntelGpuDriverSource Gpu() => new(new HttpClient(_http));

    private IntelChipsetDriverSource ChipsetSource() => new(new HttpClient(_http));

    private static DriverQuery Query(string name) => new(name, Laptop: false, DriverBranch.GameReady);

    [Fact]
    public void The_latest_arc_driver_comes_with_the_sha512_intel_publishes()
    {
        var offer = Gpu().FindLatest(Query("Intel(R) Arc(TM) B580 Graphics"));

        Assert.NotNull(offer);
        Assert.Equal("32.0.101.9150", offer.Version);
        Assert.Equal("https://downloadmirror.intel.com/930514/gfx_win_101.9150.exe", offer.DownloadUrl.ToString());
        Assert.Equal(GraphicsSha512, offer.Sha512, ignoreCase: true);
    }

    [Theory]
    [InlineData("Intel(R) Arc(TM) A770 Graphics")]
    [InlineData("Intel(R) Arc(TM) 140V GPU (16GB)")]
    [InlineData("Intel(R) Graphics")]
    public void Arc_and_core_ultra_graphics_are_looked_up(string name) =>
        Assert.Equal("32.0.101.9150", Gpu().FindLatest(Query(name))?.Version);

    [Theory]
    [InlineData("Intel(R) Iris(R) Xe Graphics")]
    [InlineData("Intel(R) UHD Graphics 630")]
    [InlineData("Intel(R) HD Graphics 520")]
    public void Older_intel_graphics_the_arc_driver_doesnt_cover_find_nothing_without_asking_intel(string name)
    {
        Assert.Null(Gpu().FindLatest(Query(name)));
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public void A_graphics_page_without_a_sha512_fails_the_lookup_so_nothing_unchecked_is_downloaded()
    {
        _http.Bodies[GraphicsPage] = Graphics.Replace($"SHA512: {GraphicsSha512}", "");

        Assert.ThrowsAny<Exception>(() => Gpu().FindLatest(Query("Intel(R) Arc(TM) B580 Graphics")));
    }

    [Fact]
    public void A_download_link_off_intels_servers_is_refused()
    {
        _http.Bodies[GraphicsPage] = Graphics.Replace("https://downloadmirror.intel.com/", "https://intel.example.com/");

        Assert.ThrowsAny<Exception>(() => Gpu().FindLatest(Query("Intel(R) Arc(TM) B580 Graphics")));
    }

    [Fact]
    public void The_graphics_installer_runs_silently_and_is_signed_by_intel()
    {
        var installer = Gpu().Installer;

        Assert.Equal(["-s", "--overwrite"], installer.Arguments);
        Assert.Equal("Intel Corporation", installer.Signer);
    }

    [Fact]
    public void The_latest_chipset_driver_comes_with_the_sha256_intel_publishes()
    {
        var offer = ChipsetSource().FindLatest(Query("Intel chipset"));

        Assert.NotNull(offer);
        Assert.Equal("10.1.20658.8883", offer.Version);
        Assert.Equal("https://downloadmirror.intel.com/925343/SetupChipset.exe", offer.DownloadUrl.ToString());
        Assert.Equal(ChipsetSha256, offer.Sha256);
        Assert.Equal(DriverKind.Chipset, ChipsetSource().Kind);
    }

    [Fact]
    public void A_chipset_page_without_a_hash_fails_the_lookup()
    {
        _http.Bodies[ChipsetPage] = Chipset.Replace($"SHA256: {ChipsetSha256}", "");

        Assert.ThrowsAny<Exception>(() => ChipsetSource().FindLatest(Query("Intel chipset")));
    }

    [Fact]
    public void A_chipset_page_without_the_installer_link_fails_the_lookup()
    {
        _http.Bodies[ChipsetPage] = Chipset.Replace("SetupChipset.exe\"", "Other.exe\"");

        Assert.ThrowsAny<Exception>(() => ChipsetSource().FindLatest(Query("Intel chipset")));
    }

    [Fact]
    public void The_chipset_installer_runs_without_ui_or_restarting()
    {
        var installer = ChipsetSource().Installer;

        Assert.Equal(["/exenoui", "/qn", "/norestart"], installer.Arguments);
        Assert.Equal("Intel Corporation", installer.Signer);
    }
}
