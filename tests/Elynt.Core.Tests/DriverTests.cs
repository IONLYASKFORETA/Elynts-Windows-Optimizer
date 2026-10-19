using Elynt.Core.Drivers;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>GPU driver detection, lookup and install, driven through the engine with fake driver adapters.</summary>
public sealed class DriverTests : IDisposable
{
    private const string Rtx4070 = @"PCI\VEN_10DE&DEV_2786&SUBSYS_51721462&REV_A1";

    private readonly FakeDriverInventory _inventory = new()
    {
        Adapters = [new DisplayAdapter("NVIDIA GeForce RTX 4070", Rtx4070, "32.0.15.9174")],
    };
    private readonly FakeDriverSource _nvidia = new();
    private static readonly byte[] PackageBytes = "pretend NVIDIA driver package"u8.ToArray();
    private const string PackageSha512 = "385dd1bb511a7a1a7f888aaa38f840a74ca8ba0258953a022f5129a06e80082ca632a2f68f0d1c629a76af5ef518bc070a2ccb9d23a0c4c215dde2cbbbd2e27c";

    private readonly FakeSystemInfo _system = new();
    private readonly FakeDownloader _downloader = new() { Content = PackageBytes };
    private readonly FakeSignatureVerifier _signatures = new();
    private readonly FakeInstallerRunner _installer = new();
    private readonly string _downloads = Directory.CreateTempSubdirectory("elynt-drivers-").FullName;

    public void Dispose() => Directory.Delete(_downloads, recursive: true);

    private Engine Engine() => new(
        Catalogue.Default,
        new SystemAdapters(new FakeRegistry(), _system, Drivers: new DriverAdapters(
            _inventory, [_nvidia], _downloads, _downloader, _signatures, _installer)),
        new ChangeJournal(new InMemoryJournalStore()),
        "S-1-5-21-test");

    [Theory]
    [InlineData("32.0.15.9174", "591.74")]
    [InlineData("31.0.15.3623", "536.23")]
    public void An_nvidia_driver_shows_its_version_in_nvidias_own_format(string windowsVersion, string nvidiaVersion)
    {
        _inventory.Adapters = [new DisplayAdapter("NVIDIA GeForce RTX 4070", Rtx4070, windowsVersion)];

        var gpu = Assert.Single(Engine().GetDrivers().Gpus);

        Assert.Equal(GpuVendor.Nvidia, gpu.Vendor);
        Assert.Equal(nvidiaVersion, gpu.DriverVersion);
        Assert.Equal("NVIDIA GeForce RTX 4070", gpu.Name);
    }

    [Fact]
    public void Checking_finds_the_latest_game_ready_driver_and_says_an_update_is_available()
    {
        _nvidia.Offers[DriverBranch.GameReady] = Offer("617.42");

        var check = Engine().CheckDriver(new DriverRequest(Rtx4070));

        Assert.True(check.Found, check.Message);
        Assert.Equal("617.42", check.Latest?.Version);
        Assert.True(check.UpdateAvailable);
        Assert.Equal(new DriverQuery("NVIDIA GeForce RTX 4070", Laptop: false, DriverBranch.GameReady), _nvidia.LastQuery);
    }

    [Fact]
    public void Checking_for_studio_asks_for_the_studio_branch()
    {
        _nvidia.Offers[DriverBranch.Studio] = Offer("591.74", DriverBranch.Studio);

        var check = Engine().CheckDriver(new DriverRequest(Rtx4070, DriverBranch.Studio));

        Assert.Equal(DriverBranch.Studio, _nvidia.LastQuery?.Branch);
        Assert.False(check.UpdateAvailable);
    }

    [Fact]
    public void A_laptop_asks_for_the_notebook_driver()
    {
        _system.Hardware |= HardwareTraits.Laptop;
        _nvidia.Offers[DriverBranch.GameReady] = Offer("617.42");

        Engine().CheckDriver(new DriverRequest(Rtx4070));

        Assert.True(_nvidia.LastQuery?.Laptop);
    }

    [Fact]
    public void A_failed_lookup_links_to_the_vendor_page_instead()
    {
        _nvidia.Fail = new HttpRequestException("Timed out.");

        var check = Engine().CheckDriver(new DriverRequest(Rtx4070));

        Assert.False(check.Found);
        Assert.Equal("https://www.nvidia.com/en-us/drivers/", check.VendorPage);
        Assert.Contains("Timed out.", check.Message);
    }

    [Fact]
    public void A_gpu_the_lookup_has_no_driver_for_links_to_the_vendor_page()
    {
        var check = Engine().CheckDriver(new DriverRequest(Rtx4070));

        Assert.False(check.Found);
        Assert.Equal("https://www.nvidia.com/en-us/drivers/", check.VendorPage);
    }

    [Fact]
    public void Installing_downloads_the_latest_driver_and_runs_it_silently()
    {
        _nvidia.Offers[DriverBranch.GameReady] = Offer("617.42");

        var result = Engine().InstallDriver(new DriverRequest(Rtx4070));

        Assert.True(result.Succeeded, result.Message);
        Assert.False(result.RebootNeeded);
        Assert.Equal("617.42", result.Version);
        Assert.Equal(Offer("617.42").DownloadUrl, _downloader.LastUrl);
        var run = Assert.Single(_installer.Runs);
        Assert.Equal(["-s", "-noreboot"], run.Arguments);
        Assert.Equal(PackageBytes, run.Content);
    }

    [Fact]
    public void Installing_reports_each_step_in_order()
    {
        _nvidia.Offers[DriverBranch.GameReady] = Offer("617.42");
        var stages = new List<DriverInstallStage>();

        Engine().InstallDriver(new DriverRequest(Rtx4070), new Steps(p => stages.Add(p.Stage)));

        Assert.Equal(
            [DriverInstallStage.LookingUp, DriverInstallStage.Downloading, DriverInstallStage.Verifying, DriverInstallStage.Installing],
            stages);
    }

    [Fact]
    public void A_rejected_download_never_reports_installing()
    {
        _nvidia.Offers[DriverBranch.GameReady] = Offer("617.42");
        _signatures.Result = new SignatureCheck(Trusted: false, Signer: null);
        var stages = new List<DriverInstallStage>();

        Engine().InstallDriver(new DriverRequest(Rtx4070), new Steps(p => stages.Add(p.Stage)));

        Assert.DoesNotContain(DriverInstallStage.Installing, stages);
    }

    private sealed class Steps(Action<DriverInstallProgress> report) : IProgress<DriverInstallProgress>
    {
        public void Report(DriverInstallProgress value) => report(value);
    }

    [Fact]
    public void A_clean_install_asks_the_installer_to_reset_its_settings()
    {
        _nvidia.Offers[DriverBranch.GameReady] = Offer("617.42");

        Engine().InstallDriver(new DriverRequest(Rtx4070, CleanInstall: true));

        Assert.Contains("-clean", Assert.Single(_installer.Runs).Arguments);
    }

    [Fact]
    public void An_unsigned_download_is_rejected_deleted_and_never_run()
    {
        _nvidia.Offers[DriverBranch.GameReady] = Offer("617.42");
        _signatures.Result = new SignatureCheck(Trusted: false, Signer: null);

        var result = Engine().InstallDriver(new DriverRequest(Rtx4070));

        Assert.False(result.Succeeded);
        Assert.Contains("signed", result.Message);
        Assert.Equal("https://www.nvidia.com/en-us/drivers/", result.VendorPage);
        Assert.Empty(_installer.Runs);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_downloads));
    }

    [Fact]
    public void A_download_signed_by_someone_other_than_the_vendor_is_rejected()
    {
        _nvidia.Offers[DriverBranch.GameReady] = Offer("617.42");
        _signatures.Result = new SignatureCheck(Trusted: true, Signer: "Totally NVIDIA Ltd");

        var result = Engine().InstallDriver(new DriverRequest(Rtx4070));

        Assert.False(result.Succeeded);
        Assert.Contains("Totally NVIDIA Ltd", result.Message);
        Assert.Empty(_installer.Runs);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_downloads));
    }

    [Fact]
    public void A_download_that_doesnt_match_the_vendors_published_hash_is_rejected()
    {
        _nvidia.Offers[DriverBranch.GameReady] = Offer("617.42") with { Sha512 = new string('0', 128) };

        var result = Engine().InstallDriver(new DriverRequest(Rtx4070));

        Assert.False(result.Succeeded);
        Assert.Contains("hash", result.Message);
        Assert.Empty(_installer.Runs);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_downloads));
    }

    [Fact]
    public void A_download_that_matches_the_vendors_published_hash_installs()
    {
        _nvidia.Offers[DriverBranch.GameReady] = Offer("617.42") with { Sha512 = PackageSha512.ToUpperInvariant() };

        Assert.True(Engine().InstallDriver(new DriverRequest(Rtx4070)).Succeeded);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3010)]
    [InlineData(1641)]
    public void Reboot_needed_exit_codes_succeed_and_ask_for_a_reboot(int exitCode)
    {
        _nvidia.Offers[DriverBranch.GameReady] = Offer("617.42");
        _installer.ExitCode = exitCode;

        var result = Engine().InstallDriver(new DriverRequest(Rtx4070));

        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.RebootNeeded);
        Assert.Contains("Restart", result.Message);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1603)]
    [InlineData(-1)]
    public void Other_exit_codes_fail(int exitCode)
    {
        _nvidia.Offers[DriverBranch.GameReady] = Offer("617.42");
        _installer.ExitCode = exitCode;

        var result = Engine().InstallDriver(new DriverRequest(Rtx4070));

        Assert.False(result.Succeeded);
        Assert.False(result.RebootNeeded);
        Assert.Contains(exitCode.ToString(), result.Message);
    }

    [Fact]
    public void Installing_when_the_lookup_fails_downloads_nothing_and_links_to_the_vendor_page()
    {
        _nvidia.Fail = new HttpRequestException("Timed out.");

        var result = Engine().InstallDriver(new DriverRequest(Rtx4070));

        Assert.False(result.Succeeded);
        Assert.Equal("https://www.nvidia.com/en-us/drivers/", result.VendorPage);
        Assert.Null(_downloader.LastUrl);
    }

    [Theory]
    [InlineData(@"..\..\Windows\System32\evil")]
    [InlineData("617.42/../../x")]
    [InlineData("617.42:stream")]
    [InlineData("617.42\n")]
    public void A_version_from_the_lookup_that_isnt_a_plain_version_is_refused_before_anything_is_downloaded(string version)
    {
        _nvidia.Offers[DriverBranch.GameReady] = Offer("617.42") with { Version = version };

        var result = Engine().InstallDriver(new DriverRequest(Rtx4070));

        Assert.False(result.Succeeded);
        Assert.Null(_downloader.LastUrl);
        Assert.Empty(_installer.Runs);
    }

    [Fact]
    public void A_failed_download_installs_nothing()
    {
        _nvidia.Offers[DriverBranch.GameReady] = Offer("617.42");
        _downloader.Fail = new IOException("Connection reset.");

        var result = Engine().InstallDriver(new DriverRequest(Rtx4070));

        Assert.False(result.Succeeded);
        Assert.Contains("Connection reset.", result.Message);
        Assert.Empty(_installer.Runs);
    }

    private static DriverOffer Offer(string version, DriverBranch branch = DriverBranch.GameReady) => new(
        version, branch, new Uri($"https://us.download.nvidia.com/Windows/{version}/{version}-desktop-win10-win11-64bit-international-dch-whql.exe"));
}
