using Elynt.Core.Drivers;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>Driver cleaning (pnputil and DDU) and problem-driver warnings, driven through the engine with fakes.</summary>
public sealed class DriverCleaningTests : IDisposable
{
    private const string DisplayClass = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string NetClass = "{4d36e972-e325-11ce-bfc1-08002be10318}";

    private static readonly DriverPackage NvidiaDisplay = new("oem12.inf", "nv_dispi.inf", "NVIDIA", DisplayClass, "32.0.15.9174");
    private static readonly DriverPackage OldNvidiaDisplay = new("oem7.inf", "nv_dispi.inf", "NVIDIA", DisplayClass, "32.0.15.6094");
    private static readonly DriverPackage IntelDisplay = new("oem3.inf", "iigd_dch.inf", "Intel Corporation", DisplayClass, "32.0.101.6078");
    private static readonly DriverPackage IntelWifi = new("oem4.inf", "netwtw08.inf", "Intel", NetClass, "23.60.0.10");

    private readonly FakeDriverStore _store = new();
    private readonly FakeKernelDrivers _kernel = new();
    private readonly FakeDduSource _ddu = new();
    private readonly FakeDduRunner _runner = new();
    private readonly FakeDownloader _downloader = new() { Content = "pretend DDU setup"u8.ToArray() };
    private readonly FakeSignatureVerifier _signatures = new() { Result = new(true, "Wagnardsoft") };
    private readonly string _root = Directory.CreateTempSubdirectory("elynt-cleaning-").FullName;
    private readonly FakeGameSources _pc = new();
    private readonly Games.InMemoryGameStore _games = new();

    public DriverCleaningTests()
    {
        _store.Installed.AddRange([NvidiaDisplay, OldNvidiaDisplay, IntelDisplay, IntelWifi]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Backups => Path.Combine(_root, "backups");

    private Engine Engine() => new(
        Catalogue.Default,
        new SystemAdapters(new FakeRegistry(), new FakeSystemInfo(), Games: _pc)
        {
            DriverCleaning = new DriverCleaningAdapters(
                _store, _kernel, Backups, _ddu, Path.Combine(_root, "ddu"), _downloader, _signatures, _runner),
        },
        new ChangeJournal(new InMemoryJournalStore()),
        "S-1-5-21-test",
        gameStore: _games);

    [Fact]
    public void Cleaning_exports_every_display_driver_of_that_vendor_before_deleting_it()
    {
        var result = Engine().CleanDrivers(new DriverCleanRequest(GpuVendor.Nvidia));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(["export oem12.inf", "export oem7.inf", "delete oem12.inf", "delete oem7.inf"], _store.Calls);
        Assert.Equal([IntelDisplay, IntelWifi], _store.Installed);
        Assert.NotNull(result.BackupId);
    }

    [Fact]
    public void When_any_export_fails_nothing_is_deleted()
    {
        _store.FailExportOf = "oem7.inf";

        var result = Engine().CleanDrivers(new DriverCleanRequest(GpuVendor.Nvidia));

        Assert.False(result.Succeeded);
        Assert.Contains("nothing was removed", result.Message);
        Assert.DoesNotContain(_store.Calls, c => c.StartsWith("delete"));
        Assert.Contains(NvidiaDisplay, _store.Installed);
        Assert.Empty(Engine().GetDriverCleaning().Backups);
    }

    [Fact]
    public void A_cleaned_vendor_can_be_restored_from_its_backup()
    {
        var engine = Engine();
        var clean = engine.CleanDrivers(new DriverCleanRequest(GpuVendor.Nvidia));

        var restore = engine.RestoreDrivers(clean.BackupId!);

        Assert.True(restore.Succeeded, restore.Message);
        Assert.Equivalent(new[] { NvidiaDisplay, OldNvidiaDisplay, IntelDisplay, IntelWifi }, _store.Installed, strict: true);
        Assert.Equal("restore", _store.Calls[^1]);
    }

    [Fact]
    public void A_clean_that_stops_partway_keeps_its_backup_to_restore()
    {
        _store.FailDeleteOf = "oem7.inf";
        var engine = Engine();

        var clean = engine.CleanDrivers(new DriverCleanRequest(GpuVendor.Nvidia));
        var restore = engine.RestoreDrivers(clean.BackupId!);

        Assert.False(clean.Succeeded);
        Assert.Contains("Restore the backup", clean.Message);
        Assert.True(restore.Succeeded, restore.Message);
        Assert.Contains(NvidiaDisplay, _store.Installed);
    }

    [Fact]
    public void The_backup_is_listed_with_what_it_holds()
    {
        var clean = Engine().CleanDrivers(new DriverCleanRequest(GpuVendor.Nvidia));

        var backup = Assert.Single(Engine().GetDriverCleaning().Backups);

        Assert.Equal(clean.BackupId, backup.Id);
        Assert.Equal(GpuVendor.Nvidia, backup.Vendor);
        Assert.Equal(["nv_dispi.inf 32.0.15.9174", "nv_dispi.inf 32.0.15.6094"], backup.Packages);
    }

    [Fact]
    public void Restoring_a_backup_that_doesnt_exist_fails_without_touching_the_store()
    {
        var restore = Engine().RestoreDrivers(@"..\..\Windows");

        Assert.False(restore.Succeeded);
        Assert.Empty(_store.Calls);
    }

    [Fact]
    public void Ddu_is_labelled_as_a_third_party_tool_downloaded_when_used()
    {
        var label = Engine().GetDriverCleaning().DduLabel;

        Assert.Contains("third-party", label);
        Assert.Contains("Wagnardsoft", label);
        Assert.Contains("downloaded", label);
    }

    [Fact]
    public void Ddu_without_confirmation_downloads_and_changes_nothing()
    {
        var result = Engine().CleanDrivers(new DriverCleanRequest(GpuVendor.Nvidia, DriverCleanMethod.Ddu));

        Assert.False(result.Succeeded);
        Assert.Null(_downloader.LastUrl);
        Assert.Empty(_runner.Runs);
        Assert.Empty(_store.Calls);
    }

    [Fact]
    public void Confirmed_ddu_backs_up_then_downloads_installs_and_runs_ddu_silently_for_that_vendor()
    {
        var result = Engine().CleanDrivers(new DriverCleanRequest(GpuVendor.Nvidia, DriverCleanMethod.Ddu, Confirmed: true));

        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.RebootNeeded);
        Assert.NotNull(result.BackupId);
        Assert.Equal(["export oem12.inf", "export oem7.inf"], _store.Calls);
        Assert.Equal(_ddu.Offer.DownloadUrl, _downloader.LastUrl);

        Assert.Equal(2, _runner.Runs.Count);
        var setup = _runner.Runs[0];
        Assert.Equal(["/S", "/D=" + Path.Combine(_root, "ddu", "DDU")], setup.Arguments);
        var ddu = _runner.Runs[1];
        Assert.Equal(Path.Combine(_root, "ddu", "DDU", Ddu.Exe), ddu.Path);
        Assert.Equal(["-Silent", "-CleanNvidia"], ddu.Arguments);

        Assert.False(File.Exists(setup.Path), "the downloaded setup is deleted afterwards");
    }

    [Theory]
    [InlineData(GpuVendor.Amd, "-CleanAmd")]
    [InlineData(GpuVendor.Intel, "-CleanIntel")]
    public void Ddu_cleans_the_chosen_vendor_even_when_its_drivers_are_already_gone(GpuVendor vendor, string flag)
    {
        _store.Installed.Clear();

        var result = Engine().CleanDrivers(new DriverCleanRequest(vendor, DriverCleanMethod.Ddu, Confirmed: true));

        Assert.True(result.Succeeded, result.Message);
        Assert.Null(result.BackupId);
        Assert.Contains(flag, _runner.Runs[^1].Arguments);
    }

    [Theory]
    [InlineData(false, "Wagnardsoft")]
    [InlineData(true, "Someone Else")]
    public void A_ddu_download_not_signed_by_wagnardsoft_is_deleted_and_never_run(bool trusted, string signer)
    {
        _signatures.Result = new SignatureCheck(trusted, signer);

        var result = Engine().CleanDrivers(new DriverCleanRequest(GpuVendor.Nvidia, DriverCleanMethod.Ddu, Confirmed: true));

        Assert.False(result.Succeeded);
        Assert.Empty(_runner.Runs);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "ddu"), "*", SearchOption.AllDirectories));
        Assert.Equal(_ddu.ProjectPage, result.Link);
    }

    private const string SetupSha256 = "2829ae674bfd8e915c8ed6db2496a6ac2563cfa243b331dd88445ac12af92115";

    [Fact]
    public void A_ddu_download_matching_its_published_hash_runs_even_without_a_signature()
    {
        _ddu.Signer = null;
        _signatures.Result = new SignatureCheck(false, null);
        _ddu.Offer = _ddu.Offer with { Sha256 = SetupSha256.ToUpperInvariant() };

        var result = Engine().CleanDrivers(new DriverCleanRequest(GpuVendor.Nvidia, DriverCleanMethod.Ddu, Confirmed: true));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, _runner.Runs.Count);
    }

    [Fact]
    public void A_ddu_download_that_doesnt_match_its_published_hash_is_never_run()
    {
        _ddu.Offer = _ddu.Offer with { Sha256 = new string('0', 64) };

        var result = Engine().CleanDrivers(new DriverCleanRequest(GpuVendor.Nvidia, DriverCleanMethod.Ddu, Confirmed: true));

        Assert.False(result.Succeeded);
        Assert.Contains("hash", result.Message);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void A_ddu_download_with_neither_a_hash_nor_a_signer_to_check_is_never_run()
    {
        _ddu.Signer = null;

        var result = Engine().CleanDrivers(new DriverCleanRequest(GpuVendor.Nvidia, DriverCleanMethod.Ddu, Confirmed: true));

        Assert.False(result.Succeeded);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void A_failed_ddu_lookup_links_to_wagnardsoft_and_runs_nothing()
    {
        _ddu.Fail = new HttpRequestException("timed out");

        var result = Engine().CleanDrivers(new DriverCleanRequest(GpuVendor.Nvidia, DriverCleanMethod.Ddu, Confirmed: true));

        Assert.False(result.Succeeded);
        Assert.Equal(_ddu.ProjectPage, result.Link);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public void A_ddu_download_that_isnt_https_is_never_fetched()
    {
        _ddu.Offer = _ddu.Offer with { DownloadUrl = new Uri("http://www.wagnardsoft.com/DDU/download/DDU.exe") };

        var result = Engine().CleanDrivers(new DriverCleanRequest(GpuVendor.Nvidia, DriverCleanMethod.Ddu, Confirmed: true));

        Assert.False(result.Succeeded);
        Assert.Null(_downloader.LastUrl);
    }

    [Fact]
    public void Ddu_failing_reports_its_exit_code_and_keeps_the_backup()
    {
        _runner.DduExitCode = 5;

        var result = Engine().CleanDrivers(new DriverCleanRequest(GpuVendor.Nvidia, DriverCleanMethod.Ddu, Confirmed: true));

        Assert.False(result.Succeeded);
        Assert.Contains("5", result.Message);
        Assert.NotNull(result.BackupId);
    }

    private const string Rtx4070 = @"PCI\VEN_10DE&DEV_2786&SUBSYS_51721462&REV_A1";

    /// <summary>Runs the NVIDIA installer, noting which driver packages were still installed when it started.</summary>
    private sealed class SnapshotInstaller(FakeDriverStore store) : IInstallerRunner
    {
        public int ExitCode { get; set; }
        public IReadOnlyList<DriverPackage>? StoreAtInstall { get; private set; }

        public int RunInstaller(string path, IReadOnlyList<string> arguments)
        {
            StoreAtInstall = store.Installed.ToList();
            return ExitCode;
        }
    }

    private Engine EngineWithInstall(SnapshotInstaller installer, FakeSignatureVerifier? signatures = null)
    {
        var source = new FakeDriverSource();
        source.Offers[DriverBranch.GameReady] = new DriverOffer("617.42", DriverBranch.GameReady, new Uri("https://international.download.nvidia.com/Windows/617.42/617.42.exe"));
        var inventory = new FakeDriverInventory { Adapters = [new DisplayAdapter("NVIDIA GeForce RTX 4070", Rtx4070, "32.0.15.9174")] };
        return new Engine(
            Catalogue.Default,
            new SystemAdapters(new FakeRegistry(), new FakeSystemInfo(), Drivers: new DriverAdapters(
                inventory, [source], Path.Combine(_root, "downloads"), new FakeDownloader { Content = [1, 2, 3] },
                signatures ?? new FakeSignatureVerifier(), installer))
            {
                DriverCleaning = new DriverCleaningAdapters(
                    _store, _kernel, Backups, _ddu, Path.Combine(_root, "ddu"), _downloader, _signatures, _runner),
            },
            new ChangeJournal(new InMemoryJournalStore()),
            "S-1-5-21-test");
    }

    [Fact]
    public void A_reinstall_can_remove_the_old_drivers_first_after_backing_them_up()
    {
        var installer = new SnapshotInstaller(_store);

        var result = EngineWithInstall(installer).InstallDriver(new DriverRequest(Rtx4070, RemoveOldDrivers: true));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal([IntelDisplay, IntelWifi], installer.StoreAtInstall);
        Assert.Equal(["export oem12.inf", "export oem7.inf", "delete oem12.inf", "delete oem7.inf"], _store.Calls);
        Assert.Single(Engine().GetDriverCleaning().Backups);
    }

    [Fact]
    public void A_rejected_download_removes_no_old_drivers()
    {
        var installer = new SnapshotInstaller(_store);

        var result = EngineWithInstall(installer, new FakeSignatureVerifier { Result = new(false, null) })
            .InstallDriver(new DriverRequest(Rtx4070, RemoveOldDrivers: true));

        Assert.False(result.Succeeded);
        Assert.Empty(_store.Calls);
    }

    [Fact]
    public void When_the_new_driver_fails_to_install_the_old_ones_are_put_back()
    {
        var installer = new SnapshotInstaller(_store) { ExitCode = 9 };

        var result = EngineWithInstall(installer).InstallDriver(new DriverRequest(Rtx4070, RemoveOldDrivers: true));

        Assert.False(result.Succeeded);
        Assert.Contains("put back", result.Message);
        Assert.Contains(NvidiaDisplay, _store.Installed);
        Assert.Contains(OldNvidiaDisplay, _store.Installed);
    }

    [Fact]
    public void A_reinstall_without_removing_old_drivers_leaves_the_store_alone()
    {
        var installer = new SnapshotInstaller(_store);

        EngineWithInstall(installer).InstallDriver(new DriverRequest(Rtx4070));

        Assert.Empty(_store.Calls);
    }

    [Fact]
    public void A_known_problem_storage_driver_warns_with_the_games_it_stops()
    {
        _kernel.Drivers.AddRange([new KernelDriver("secnvme", "3.3.0.2003"), new KernelDriver("stornvme", "10.0.26100.1")]);

        var problem = Assert.Single(Engine().GetDriverCleaning().ProblemDrivers);

        Assert.Equal("secnvme.sys", problem.Driver);
        Assert.Contains("Samsung", problem.Name);
        Assert.Equal("3.3.0.2003", problem.Version);
        Assert.Contains("Valorant", problem.Warning);
        Assert.Contains("League of Legends", problem.Warning);
        Assert.Contains("FACEIT", problem.Warning);
    }

    [Fact]
    public void The_warning_names_the_users_own_affected_games_when_it_finds_any()
    {
        _kernel.Drivers.Add(new KernelDriver("secnvme", "3.3.0.2003"));
        _pc.File(@"C:\Games\CS2\game\bin\win64\cs2.exe");
        var engine = Engine();
        Assert.True(engine.AddGame(@"C:\Games\CS2\game\bin\win64\cs2.exe").Succeeded);

        var problem = Assert.Single(engine.GetDriverCleaning().ProblemDrivers);

        Assert.Contains("cs2 (FACEIT)", problem.Warning);
        Assert.DoesNotContain("Valorant", problem.Warning);
    }

    [Fact]
    public void Driver_names_match_whatever_their_case()
    {
        _kernel.Drivers.Add(new KernelDriver("iaStorVD", "19.5.1.1040"));

        Assert.Equal("iastorvd.sys", Assert.Single(Engine().GetDriverCleaning().ProblemDrivers).Driver);
    }

    [Theory]
    [InlineData("3.4.0.512", true)]
    [InlineData("3.5.0.0", false)]
    [InlineData("4.0.1.1", false)]
    [InlineData(null, true)]
    public void The_mediatek_wifi_driver_warns_only_below_3_5(string? version, bool warns)
    {
        _kernel.Drivers.Add(new KernelDriver("mtkwl6ex", version));

        var problems = Engine().GetDriverCleaning().ProblemDrivers;

        Assert.Equal(warns, problems.Count == 1);
        if (warns)
            Assert.Contains("3.5", problems[0].Warning);
    }

    [Fact]
    public void Cleaning_a_vendor_with_no_display_drivers_changes_nothing()
    {
        var result = Engine().CleanDrivers(new DriverCleanRequest(GpuVendor.Amd));

        Assert.False(result.Succeeded);
        Assert.Contains("no AMD display drivers", result.Message);
        Assert.Empty(_store.Calls);
    }
}
