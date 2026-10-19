using Elynt.Core.Drivers;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>The optional lean NVIDIA install, driven through the engine with a fake package and installer.</summary>
public sealed class LeanNvidiaInstallTests : IDisposable
{
    private const string Rtx4070 = @"PCI\VEN_10DE&DEV_2786&SUBSYS_51721462&REV_A1";

    private readonly FakeDriverInventory _inventory = new()
    {
        Adapters = [new DisplayAdapter("NVIDIA GeForce RTX 4070", Rtx4070, "32.0.15.9174")],
    };
    private readonly FakeDriverSource _nvidia = new();
    private readonly FakeSystemInfo _system = new();
    private readonly FakeSignatureVerifier _signatures = new();
    private readonly FakePackageExtractor _extractor = new();
    private readonly PackageRecordingInstallerRunner _installer = new();
    private readonly string _downloads = Directory.CreateTempSubdirectory("elynt-lean-").FullName;

    public LeanNvidiaInstallTests()
    {
        _nvidia.Offers[DriverBranch.GameReady] = new DriverOffer(
            "617.42", DriverBranch.GameReady, new Uri("https://international.download.nvidia.com/Windows/617.42/617.42-desktop.exe"));
    }

    public void Dispose() => Directory.Delete(_downloads, recursive: true);

    private Engine Engine() => new(
        Catalogue.Default,
        new SystemAdapters(new FakeRegistry(), _system, Drivers: new DriverAdapters(
            _inventory, [_nvidia], _downloads, new FakeDownloader { Content = "package"u8.ToArray() }, _signatures, _installer, _extractor)),
        new ChangeJournal(new InMemoryJournalStore()),
        "S-1-5-21-test");

    private static DriverRequest Lean(bool clean = false) => new(Rtx4070, CleanInstall: clean, Lean: true);

    [Fact]
    public void A_lean_install_keeps_only_the_display_driver_and_what_it_requires()
    {
        var result = Engine().InstallDriver(Lean());

        Assert.True(result.Succeeded, result.Message);
        var run = Assert.Single(_installer.Runs);
        Assert.Equal("setup.exe", Path.GetFileName(run.Path));
        Assert.Equal(["Display.Driver", "NVI2", "NvContainer", "PhysX"], run.Folders);
        Assert.Equal(["-s", "-noreboot"], run.Arguments);
    }

    [Fact]
    public void A_lean_install_never_keeps_nvidia_telemetry_even_when_a_kept_component_says_it_requires_it()
    {
        _extractor.Components["Display.Driver"] = ("Display.Driver", ["PhysX", "NvContainer.LocalSystem", "NvTelemetry.Container"]);
        _extractor.Components["PhysX"] = ("PhysX", ["NvTelemetry"]);

        var result = Engine().InstallDriver(Lean());

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(["Display.Driver", "NVI2", "NvContainer", "PhysX"], Assert.Single(_installer.Runs).Folders);
    }

    [Fact]
    public void A_lean_install_on_a_laptop_keeps_dynamic_boost_and_the_usb_c_power_controller()
    {
        _system.Hardware |= HardwareTraits.Laptop;

        var result = Engine().InstallDriver(Lean());

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(["Display.Driver", "NVI2", "NVPCF", "NvContainer", "PPC", "PhysX"], Assert.Single(_installer.Runs).Folders);
    }

    [Fact]
    public void A_lean_install_removes_the_consent_entries_setup_would_fail_on_and_blanks_the_installer_telemetry_urls()
    {
        Engine().InstallDriver(Lean());

        var run = Assert.Single(_installer.Runs);
        Assert.DoesNotContain("EulaHtmlFile", run.SetupCfg);
        Assert.DoesNotContain("FunctionalConsentFile", run.SetupCfg);
        Assert.DoesNotContain("PrivacyPolicyFile", run.SetupCfg);
        Assert.Contains("ListDevices.txt", run.SetupCfg);
        Assert.DoesNotContain("https://", run.PresentationsCfg);
        Assert.Contains("NVIDIA Installer", run.PresentationsCfg);
    }

    [Fact]
    public void A_lean_clean_install_adds_the_clean_switch()
    {
        Engine().InstallDriver(Lean(clean: true));

        Assert.Equal(["-s", "-noreboot", "-clean"], Assert.Single(_installer.Runs).Arguments);
    }

    [Fact]
    public void A_lean_install_reports_a_slimming_step_between_verifying_and_installing()
    {
        var steps = new List<DriverInstallStage>();

        Engine().InstallDriver(Lean(), new SyncProgress(p => steps.Add(p.Stage)));

        Assert.Equal(
            [DriverInstallStage.LookingUp, DriverInstallStage.Downloading, DriverInstallStage.Verifying, DriverInstallStage.Slimming, DriverInstallStage.Installing],
            steps);
    }

    [Fact]
    public void After_a_lean_install_nothing_is_left_in_the_download_folder()
    {
        Engine().InstallDriver(Lean());

        Assert.Empty(Directory.GetFileSystemEntries(_downloads));
    }

    [Fact]
    public void When_unpacking_fails_nothing_is_installed_and_the_download_is_deleted()
    {
        _extractor.Fail = new IOException("Archive is corrupt.");

        var result = Engine().InstallDriver(Lean());

        Assert.False(result.Succeeded);
        Assert.Contains("Archive is corrupt.", result.Message);
        Assert.Contains("untick lean install", result.Message);
        Assert.Empty(_installer.Runs);
        Assert.Empty(Directory.GetFileSystemEntries(_downloads));
    }

    [Fact]
    public void An_unpacked_setup_not_signed_by_nvidia_is_never_run()
    {
        var verifier = new UnpackedSetupVerifier();
        var engine = new Engine(
            Catalogue.Default,
            new SystemAdapters(new FakeRegistry(), _system, Drivers: new DriverAdapters(
                _inventory, [_nvidia], _downloads, new FakeDownloader { Content = "package"u8.ToArray() }, verifier, _installer, _extractor)),
            new ChangeJournal(new InMemoryJournalStore()),
            "S-1-5-21-test");

        var result = engine.InstallDriver(Lean());

        Assert.False(result.Succeeded);
        Assert.Contains("isn't signed by NVIDIA Corporation", result.Message);
        Assert.Empty(_installer.Runs);
    }

    [Fact]
    public void A_package_without_a_display_driver_is_refused_rather_than_installed_empty()
    {
        _extractor.Components.Remove("Display.Driver");

        var result = Engine().InstallDriver(Lean());

        Assert.False(result.Succeeded);
        Assert.Contains("no display driver", result.Message);
        Assert.Empty(_installer.Runs);
    }

    [Fact]
    public void Without_an_extractor_a_lean_install_is_refused()
    {
        var engine = new Engine(
            Catalogue.Default,
            new SystemAdapters(new FakeRegistry(), _system, Drivers: new DriverAdapters(
                _inventory, [_nvidia], _downloads, new FakeDownloader { Content = "package"u8.ToArray() }, _signatures, _installer)),
            new ChangeJournal(new InMemoryJournalStore()),
            "S-1-5-21-test");

        var result = engine.InstallDriver(Lean());

        Assert.False(result.Succeeded);
        Assert.Contains("Lean install isn't available", result.Message);
        Assert.Empty(_installer.Runs);
    }

    [Fact]
    public void A_normal_install_runs_the_downloaded_package_untouched()
    {
        Engine().InstallDriver(new DriverRequest(Rtx4070));

        Assert.Null(_extractor.LastArchive);
        Assert.EndsWith(".exe", Assert.Single(_installer.Runs).Path);
        Assert.NotEqual("setup.exe", Path.GetFileName(_installer.Runs[0].Path));
    }

    private sealed class SyncProgress(Action<DriverInstallProgress> report) : IProgress<DriverInstallProgress>
    {
        public void Report(DriverInstallProgress value) => report(value);
    }

    /// <summary>The download is NVIDIA's, but the setup.exe inside it isn't.</summary>
    private sealed class UnpackedSetupVerifier : ISignatureVerifier
    {
        public SignatureCheck Check(string path) => Path.GetFileName(path) == "setup.exe"
            ? new SignatureCheck(Trusted: true, Signer: "Someone Else Ltd")
            : new SignatureCheck(Trusted: true, Signer: "NVIDIA Corporation");
    }
}
