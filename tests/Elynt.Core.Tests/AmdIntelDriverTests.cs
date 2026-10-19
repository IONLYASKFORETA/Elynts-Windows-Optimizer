using Elynt.Core.Drivers;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>AMD and Intel GPU drivers and the AMD and Intel chipset drivers, driven through the engine with fakes.</summary>
public sealed class AmdIntelDriverTests : IDisposable
{
    private const string Rx7800 = @"PCI\VEN_1002&DEV_747E&SUBSYS_E3321DA2&REV_C8";
    private const string ArcB580 = @"PCI\VEN_8086&DEV_E20B&SUBSYS_10008086&REV_00";
    private const string AmdPage = "https://www.amd.com/en/support/download/drivers.html";
    private const string IntelGraphicsPage = "https://www.intel.com/content/www/us/en/download/785597/intel-arc-graphics-windows.html";
    private const string IntelChipsetPage = "https://www.intel.com/content/www/us/en/download/19347/chipset-inf-utility.html";

    private static readonly byte[] PackageBytes = "pretend driver package"u8.ToArray();
    private const string PackageSha256 = "1d0f72032585082f6c13b378e17262e79a95cc90d84c1c36014ce821b8dc5374";
    private const string PackageSha512 = "78478455eb1473db9e7b6d4960b00fac4fb4e5d6735ee57c2a324c98c00ae9df27ef2b403cf895944fc47d980c67fe50c7860dc75cdddf132f10fde9a94962b8";

    private readonly FakeDriverInventory _inventory = new();
    private readonly FakeSystemInfo _system = new();
    private readonly FakeDriverSource _amdGpu = new(GpuVendor.Amd, DriverKind.Graphics, AmdPage,
        new InstallerProfile("Advanced Micro Devices, Inc.", ["-INSTALL"], [], InstallerProfile.StandardRebootCodes));
    private readonly FakeDriverSource _amdChipset = new(GpuVendor.Amd, DriverKind.Chipset, AmdPage,
        new InstallerProfile("Advanced Micro Devices, Inc.", ["/S"], [], InstallerProfile.StandardRebootCodes));
    private readonly FakeDriverSource _intelGpu = new(GpuVendor.Intel, DriverKind.Graphics, IntelGraphicsPage,
        new InstallerProfile("Intel Corporation", ["-s", "--overwrite"], [], InstallerProfile.StandardRebootCodes));
    private readonly FakeDriverSource _intelChipset = new(GpuVendor.Intel, DriverKind.Chipset, IntelChipsetPage,
        new InstallerProfile("Intel Corporation", ["/exenoui", "/qn", "/norestart"], [], InstallerProfile.StandardRebootCodes));
    private readonly FakeDownloader _downloader = new() { Content = PackageBytes };
    private readonly FakeSignatureVerifier _signatures = new();
    private readonly FakeInstallerRunner _installer = new();
    private readonly string _downloads = Directory.CreateTempSubdirectory("elynt-drivers-").FullName;

    public void Dispose() => Directory.Delete(_downloads, recursive: true);

    private Engine Engine() => new(
        Catalogue.Default,
        new SystemAdapters(new FakeRegistry(), _system, Drivers: new DriverAdapters(
            _inventory, [new FakeDriverSource(), _amdGpu, _amdChipset, _intelGpu, _intelChipset], _downloads, _downloader, _signatures, _installer)),
        new ChangeJournal(new InMemoryJournalStore()),
        "S-1-5-21-test");

    private void HaveRadeon(string? adrenalin = "25.9.1") =>
        _inventory.Adapters = [new DisplayAdapter("AMD Radeon RX 7800 XT", Rx7800, "32.0.21025.1024", adrenalin)];

    private void HaveArc(string version = "32.0.101.8826") =>
        _inventory.Adapters = [new DisplayAdapter("Intel(R) Arc(TM) B580 Graphics", ArcB580, version)];

    private static DriverOffer AmdOffer(string version = "26.8.1") => new(
        version, DriverBranch.GameReady,
        new Uri($"https://drivers.amd.com/drivers/whql-amd-software-adrenalin-edition-{version}-win11-c.exe"),
        Referer: new Uri(AmdPage));

    [Fact]
    public void An_amd_driver_shows_its_adrenalin_version()
    {
        HaveRadeon();

        var gpu = Assert.Single(Engine().GetDrivers().Gpus);

        Assert.Equal(GpuVendor.Amd, gpu.Vendor);
        Assert.Equal("25.9.1", gpu.DriverVersion);
    }

    [Fact]
    public void Checking_an_amd_gpu_finds_the_newer_adrenalin_driver()
    {
        HaveRadeon();
        _amdGpu.Offers[DriverBranch.GameReady] = AmdOffer();

        var check = Engine().CheckDriver(new DriverRequest(Rx7800));

        Assert.True(check.Found, check.Message);
        Assert.True(check.UpdateAvailable);
        Assert.Contains("26.8.1", check.Message);
    }

    [Fact]
    public void An_amd_gpu_already_on_the_latest_adrenalin_driver_is_up_to_date()
    {
        HaveRadeon("26.8.1");
        _amdGpu.Offers[DriverBranch.GameReady] = AmdOffer();

        Assert.False(Engine().CheckDriver(new DriverRequest(Rx7800)).UpdateAvailable);
    }

    [Fact]
    public void When_the_adrenalin_version_is_unknown_the_check_says_so_and_still_offers_the_driver()
    {
        HaveRadeon(adrenalin: null);
        _amdGpu.Offers[DriverBranch.GameReady] = AmdOffer();

        var check = Engine().CheckDriver(new DriverRequest(Rx7800));

        Assert.True(check.UpdateAvailable);
        Assert.Contains("can't tell", check.Message);
    }

    [Fact]
    public void A_failed_amd_lookup_shows_amds_page_and_downloads_nothing()
    {
        HaveRadeon();
        _amdGpu.Fail = new InvalidDataException("AMD's drivers page no longer links an installer the app recognises.");

        var check = Engine().CheckDriver(new DriverRequest(Rx7800));
        var install = Engine().InstallDriver(new DriverRequest(Rx7800));

        Assert.False(check.Found);
        Assert.Null(check.Latest);
        Assert.Equal(AmdPage, check.VendorPage);
        Assert.False(install.Succeeded);
        Assert.Equal(AmdPage, install.VendorPage);
        Assert.Null(_downloader.LastUrl);
    }

    [Fact]
    public void A_radeon_the_lookup_doesnt_cover_shows_amds_page()
    {
        HaveRadeon();

        var check = Engine().CheckDriver(new DriverRequest(Rx7800));

        Assert.False(check.Found);
        Assert.Equal(AmdPage, check.VendorPage);
    }

    [Fact]
    public void Installing_an_amd_driver_downloads_it_from_amds_page_and_runs_it_silently()
    {
        HaveRadeon();
        _amdGpu.Offers[DriverBranch.GameReady] = AmdOffer();
        _signatures.Result = new SignatureCheck(true, "Advanced Micro Devices, Inc.");

        var result = Engine().InstallDriver(new DriverRequest(Rx7800));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(new Uri(AmdPage), _downloader.LastReferer);
        Assert.Equal(["-INSTALL"], Assert.Single(_installer.Runs).Arguments);
    }

    [Fact]
    public void An_amd_download_signed_by_someone_else_is_refused_and_shows_amds_page()
    {
        HaveRadeon();
        _amdGpu.Offers[DriverBranch.GameReady] = AmdOffer();
        _signatures.Result = new SignatureCheck(true, "NVIDIA Corporation");

        var result = Engine().InstallDriver(new DriverRequest(Rx7800));

        Assert.False(result.Succeeded);
        Assert.Equal(AmdPage, result.VendorPage);
        Assert.Empty(_installer.Runs);
    }

    [Fact]
    public void An_intel_driver_compares_against_windows_own_version()
    {
        HaveArc();
        _intelGpu.Offers[DriverBranch.GameReady] = IntelOffer();

        var check = Engine().CheckDriver(new DriverRequest(ArcB580));

        Assert.Equal("32.0.101.8826", check.Gpu?.DriverVersion);
        Assert.True(check.UpdateAvailable);
    }

    [Fact]
    public void An_intel_download_that_doesnt_match_the_published_sha512_is_refused()
    {
        HaveArc();
        _intelGpu.Offers[DriverBranch.GameReady] = IntelOffer() with { Sha512 = new string('a', 128) };
        _signatures.Result = new SignatureCheck(true, "Intel Corporation");

        var result = Engine().InstallDriver(new DriverRequest(ArcB580));

        Assert.False(result.Succeeded);
        Assert.Contains("SHA-512", result.Message);
        Assert.Empty(_installer.Runs);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_downloads));
    }

    [Fact]
    public void An_amd_cpu_shows_the_amd_chipset_driver_and_its_installed_version()
    {
        _system.Hardware = HardwareTraits.AmdCpu;
        _inventory.ChipsetVersions[GpuVendor.Amd] = "7.06.02.123";

        var chipset = Engine().GetDrivers().Chipset;

        Assert.NotNull(chipset);
        Assert.Equal(GpuVendor.Amd, chipset.Vendor);
        Assert.Equal("7.06.02.123", chipset.DriverVersion);
    }

    [Fact]
    public void A_pc_whose_cpu_maker_is_unknown_shows_no_chipset_driver()
    {
        _system.Hardware = HardwareTraits.NvidiaGpu;

        Assert.Null(Engine().GetDrivers().Chipset);
    }

    [Fact]
    public void Checking_the_chipset_asks_the_chipset_lookup_for_the_cpus_maker()
    {
        _system.Hardware = HardwareTraits.AmdCpu;
        _inventory.ChipsetVersions[GpuVendor.Amd] = "7.06.02.123";
        _amdChipset.Offers[DriverBranch.GameReady] = new("8.08.12.551", DriverBranch.GameReady,
            new Uri("https://drivers.amd.com/drivers/AMD_Chipset_Software_8.08.12.551.exe"), Referer: new Uri(AmdPage));

        var check = Engine().CheckDriver(new DriverRequest(DriverRequest.Chipset));

        Assert.True(check.Found, check.Message);
        Assert.True(check.UpdateAvailable);
        Assert.Null(_amdGpu.LastQuery);
    }

    [Fact]
    public void A_chipset_driver_that_isnt_installed_is_offered()
    {
        _system.Hardware = HardwareTraits.IntelCpu;
        _intelChipset.Offers[DriverBranch.GameReady] = IntelChipsetOffer();

        var check = Engine().CheckDriver(new DriverRequest(DriverRequest.Chipset));

        Assert.True(check.UpdateAvailable);
    }

    [Fact]
    public void Installing_the_intel_chipset_driver_checks_its_sha256_and_runs_it_without_ui()
    {
        _system.Hardware = HardwareTraits.IntelCpu;
        _intelChipset.Offers[DriverBranch.GameReady] = IntelChipsetOffer();
        _signatures.Result = new SignatureCheck(true, "Intel Corporation");

        var result = Engine().InstallDriver(new DriverRequest(DriverRequest.Chipset));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(["/exenoui", "/qn", "/norestart"], Assert.Single(_installer.Runs).Arguments);
    }

    [Fact]
    public void An_intel_chipset_download_that_doesnt_match_the_published_sha256_is_refused()
    {
        _system.Hardware = HardwareTraits.IntelCpu;
        _intelChipset.Offers[DriverBranch.GameReady] = IntelChipsetOffer() with { Sha256 = new string('0', 64) };
        _signatures.Result = new SignatureCheck(true, "Intel Corporation");

        var result = Engine().InstallDriver(new DriverRequest(DriverRequest.Chipset));

        Assert.False(result.Succeeded);
        Assert.Contains("SHA-256", result.Message);
        Assert.Equal(IntelChipsetPage, result.VendorPage);
        Assert.Empty(_installer.Runs);
    }

    private static DriverOffer IntelOffer() => new(
        "32.0.101.9150", DriverBranch.GameReady, new Uri("https://downloadmirror.intel.com/930514/gfx_win_101.9150.exe"),
        Sha512: PackageSha512);

    private static DriverOffer IntelChipsetOffer() => new(
        "10.1.20658.8883", DriverBranch.GameReady, new Uri("https://downloadmirror.intel.com/925343/SetupChipset.exe"),
        Sha256: PackageSha256);
}
