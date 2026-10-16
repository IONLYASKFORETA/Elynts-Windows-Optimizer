using Elynt.Core.Journal;
using Elynt.Core.Repair;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>The Repair section, driven through the Engine's repair commands against a fake PC.</summary>
public class RepairTests
{
    private readonly FakeRepairSystem _pc = new();
    private readonly InMemoryRepairLogStore _logs = new();

    private Engine NewEngine() => new(
        Catalogue.Default,
        new SystemAdapters(new FakeRegistry(), new FakeSystemInfo(), Repair: _pc),
        new ChangeJournal(new InMemoryJournalStore()),
        "S-1-5-21-test",
        repairLogs: _logs);

    private RepairToolView Tool(string id) => NewEngine().ListRepairTools().Single(t => t.Id == id);

    [Fact]
    public void Every_repair_tool_says_up_front_whether_it_needs_a_restart()
    {
        Assert.Equal(RestartNeed.None, Tool(RepairToolIds.SystemFiles).Restart);
        Assert.Equal(RestartNeed.None, Tool(RepairToolIds.DiskCheck).Restart);
        Assert.Equal(RestartNeed.Restart, Tool(RepairToolIds.DiskFixAtRestart).Restart);
        Assert.Equal(RestartNeed.Restart, Tool(RepairToolIds.NetworkRepair).Restart);
        Assert.Equal(RestartNeed.Restart, Tool(RepairToolIds.FullNetworkReset).Restart);
        Assert.Equal(RestartNeed.None, Tool(RepairToolIds.WindowsUpdateReset).Restart);
        Assert.Equal(RestartNeed.Restart, Tool(RepairToolIds.IconAndFontCache).Restart);
        Assert.Equal(RestartNeed.None, Tool(RepairToolIds.StoreCache).Restart);

        Assert.All(NewEngine().ListRepairTools(), tool =>
        {
            Assert.Equal(Sections.Repair, tool.Section);
            Assert.False(string.IsNullOrWhiteSpace(tool.RestartText));
            Assert.NotEmpty(tool.ExactCommands);
        });
    }

    [Fact]
    public async Task System_file_repair_runs_DISM_then_SFC_streams_progress_and_ends_with_a_plain_summary()
    {
        _pc.Script("dism.exe", 0,
            "Deployment Image Servicing and Management tool",
            "[==========                 20.0%                          ]",
            "[==========================100.0%==========================]",
            "The restore operation completed successfully.");
        _pc.Script("sfc.exe", 0,
            "Beginning system scan.  This process will take some time.",
            "Verification 45% complete.",
            "Verification 100% complete.",
            "Windows Resource Protection found corrupt files and successfully repaired them.");
        var progress = new List<RepairProgress>();

        var result = await NewEngine().RunRepairAsync(RepairToolIds.SystemFiles, confirmed: false, new Collect(progress), TestContext.Current.CancellationToken);

        Assert.Equal(["dism.exe /Online /Cleanup-Image /RestoreHealth", "sfc.exe /scannow"], _pc.Actions);
        Assert.True(result.Succeeded, result.Summary);
        Assert.Contains("Windows' component store is healthy.", result.Summary);
        Assert.Contains("Found damaged system files and repaired them.", result.Summary);
        Assert.Contains(progress, p => p.Step == 1 && p.StepCount == 2 && p.Percent == 20.0);
        Assert.Contains(progress, p => p.Step == 2 && p.Percent == 45 && p.Line == "Verification 45% complete.");
        Assert.True(progress.FindIndex(p => p.Step == 2) > progress.FindLastIndex(p => p.Step == 1));
    }

    [Fact]
    public async Task Every_run_saves_a_log_with_each_command_its_output_and_the_summary()
    {
        _pc.Script("dism.exe", 0, "The restore operation completed successfully.");
        _pc.Script("sfc.exe", 0, "Windows Resource Protection did not find any integrity violations.");

        var result = await NewEngine().RunRepairAsync(RepairToolIds.SystemFiles, confirmed: false, cancellation: TestContext.Current.CancellationToken);

        var (name, text) = Assert.Single(_logs.Saved);
        Assert.Equal(name, result.LogLocation);
        Assert.Equal(text, result.LogText);
        Assert.EndsWith(".log", name);
        Assert.Contains("Repair Windows system files", text);
        Assert.Contains("dism.exe /Online /Cleanup-Image /RestoreHealth", text);
        Assert.Contains("The restore operation completed successfully.", text);
        Assert.Contains("did not find any integrity violations", text);
        Assert.Contains("Exit code: 0", text);
        Assert.Contains(result.Summary, text);
        Assert.Contains("26100.1", text);
    }

    private Task<RepairResult> Run(string toolId, bool confirmed = false) =>
        NewEngine().RunRepairAsync(toolId, confirmed, cancellation: TestContext.Current.CancellationToken);

    [Fact]
    public async Task System_file_repair_still_runs_SFC_when_DISM_fails_and_says_so_plainly()
    {
        _pc.Script("dism.exe", unchecked((int)0x800F081F), "Error: 0x800f081f", "The source files could not be found.");
        _pc.Script("sfc.exe", 0, "Windows Resource Protection did not find any integrity violations.");

        var result = await Run(RepairToolIds.SystemFiles);

        Assert.False(result.Succeeded);
        Assert.Contains("sfc.exe /scannow", _pc.Actions);
        Assert.StartsWith("Some steps didn't work", result.Summary);
        Assert.Contains("DISM couldn't finish (error 0x800F081F)", result.Summary);
        Assert.Contains("check the internet connection", result.Summary);
        Assert.Contains("No damaged system files found.", result.Summary);
    }

    [Fact]
    public async Task A_program_that_cannot_start_fails_its_step_and_the_run_is_still_logged()
    {
        _pc.Missing("chkdsk.exe");

        var result = await Run(RepairToolIds.DiskCheck);

        Assert.False(result.Succeeded);
        Assert.Contains("chkdsk.exe wasn't found.", result.Summary);
        Assert.Contains("chkdsk.exe wasn't found.", Assert.Single(_logs.Saved).Text);
    }

    [Fact]
    public async Task The_disk_check_is_read_only_and_a_clean_disk_offers_no_fix()
    {
        _pc.SystemDrive = "D:";
        _pc.Script("chkdsk.exe", 0, "Windows has scanned the file system and found no problems.", "No further action is required.");

        var result = await Run(RepairToolIds.DiskCheck);

        Assert.Equal(["chkdsk.exe D: /scan"], _pc.Actions);
        Assert.True(result.Succeeded);
        Assert.False(result.RestartNeeded);
        Assert.Null(result.FollowUpToolId);
        Assert.Contains("No problems found", result.Summary);
    }

    [Fact]
    public async Task Disk_problems_offer_a_fix_at_the_next_restart()
    {
        _pc.Script("chkdsk.exe", 3,
            "Windows has scanned the file system and found problems.",
            "Run CHKDSK with the /F (fix) option to correct these.");

        var check = await Run(RepairToolIds.DiskCheck);

        Assert.True(check.Succeeded);
        Assert.Equal(RepairToolIds.DiskFixAtRestart, check.FollowUpToolId);
        Assert.StartsWith("Done, and it found problems", check.Summary);
        Assert.True(Tool(RepairToolIds.DiskFixAtRestart).IsFollowUp);

        var fix = await Run(RepairToolIds.DiskFixAtRestart);

        Assert.Equal("fsutil.exe dirty set C:", _pc.Actions[^1]);
        Assert.True(fix.Succeeded);
        Assert.True(fix.RestartNeeded);
        Assert.Contains("Restart the PC to finish.", fix.Summary);
    }

    [Fact]
    public async Task Network_repair_flushes_DNS_and_resets_Winsock_and_TCP_IP_but_keeps_adapters()
    {
        _pc.Script("netsh.exe winsock", 1, "Access is denied.");

        var result = await Run(RepairToolIds.NetworkRepair);

        Assert.Equal(["ipconfig.exe /flushdns", "netsh.exe winsock reset", "netsh.exe int ip reset"], _pc.Actions);
        Assert.False(result.Succeeded);
        Assert.Contains("Access is denied.", result.Summary);
        Assert.Null(Tool(RepairToolIds.NetworkRepair).Confirmation);
    }

    [Fact]
    public async Task Full_network_reset_is_opt_in_and_refused_without_confirmation()
    {
        Assert.Contains("VPN", Tool(RepairToolIds.FullNetworkReset).Confirmation);

        var refused = await Run(RepairToolIds.FullNetworkReset, confirmed: false);

        Assert.False(refused.Succeeded);
        Assert.Contains("Nothing was changed", refused.Summary);
        Assert.Empty(_pc.Actions);
        Assert.Empty(_logs.Saved);

        var done = await Run(RepairToolIds.FullNetworkReset, confirmed: true);

        Assert.Equal(["netcfg.exe -d"], _pc.Actions);
        Assert.True(done.Succeeded);
        Assert.True(done.RestartNeeded);
    }

    [Fact]
    public async Task Windows_Update_repair_stops_services_clears_both_caches_and_starts_them_again()
    {
        _pc.AddFile(RepairFolder.Windows, @"SoftwareDistribution\Download\abc\update.cab");
        _pc.AddFile(RepairFolder.Windows, @"SoftwareDistribution\DataStore\DataStore.edb");
        _pc.AddFile(RepairFolder.Windows, @"System32\catroot2\edb.log");
        _pc.AddFile(RepairFolder.Windows, @"System32\catroot\{F750E6C3}\keep.cat");
        _pc.AddFile(RepairFolder.Windows, @"System32\kernel32.dll");
        _pc.Script("net.exe stop bits", 2, "The Background Intelligent Transfer Service service is not started.");

        var result = await Run(RepairToolIds.WindowsUpdateReset);

        Assert.True(result.Succeeded, result.Summary);
        Assert.Equal(
            [
                "net.exe stop UsoSvc /y", "net.exe stop wuauserv /y", "net.exe stop bits /y", "net.exe stop cryptsvc /y",
                @"clear Windows\SoftwareDistribution\* (with subfolders)", @"clear Windows\System32\catroot2\* (with subfolders)",
                "net.exe start cryptsvc", "net.exe start bits", "net.exe start wuauserv", "net.exe start UsoSvc",
            ],
            _pc.Actions);
        Assert.False(_pc.FileExists(RepairFolder.Windows, @"SoftwareDistribution\Download\abc\update.cab"));
        Assert.False(_pc.FileExists(RepairFolder.Windows, @"System32\catroot2\edb.log"));
        Assert.True(_pc.FileExists(RepairFolder.Windows, @"System32\catroot\{F750E6C3}\keep.cat"));
        Assert.True(_pc.FileExists(RepairFolder.Windows, @"System32\kernel32.dll"));
        Assert.False(result.RestartNeeded);
    }

    [Fact]
    public async Task Windows_Update_repair_leaves_the_caches_alone_but_restarts_services_when_a_service_will_not_stop()
    {
        _pc.AddFile(RepairFolder.Windows, @"SoftwareDistribution\Download\update.cab");
        _pc.Script("net.exe stop wuauserv", 5, "System error 5 has occurred.", "Access is denied.");

        var result = await Run(RepairToolIds.WindowsUpdateReset);

        Assert.False(result.Succeeded);
        Assert.True(_pc.FileExists(RepairFolder.Windows, @"SoftwareDistribution\Download\update.cab"));
        Assert.DoesNotContain(_pc.Actions, a => a.StartsWith("clear"));
        Assert.Equal(["net.exe start cryptsvc", "net.exe start bits", "net.exe start wuauserv", "net.exe start UsoSvc"], _pc.Actions[^4..]);
        Assert.Contains("Skipped because an earlier step didn't work.", result.Summary);
    }

    [Fact]
    public async Task Icon_and_font_cache_rebuild_clears_only_the_caches_and_says_a_restart_finishes_it()
    {
        _pc.AddFile(RepairFolder.UserLocalAppData, "IconCache.db");
        _pc.AddFile(RepairFolder.UserLocalAppData, @"Microsoft\Windows\Explorer\iconcache_32.db", inUse: true);
        _pc.AddFile(RepairFolder.UserLocalAppData, @"Microsoft\Windows\Explorer\iconcache_48.db");
        _pc.AddFile(RepairFolder.UserLocalAppData, @"Microsoft\Windows\Explorer\thumbcache_32.db");
        _pc.AddFile(RepairFolder.Windows, @"ServiceProfiles\LocalService\AppData\Local\FontCache\Fonts\font.dat");
        _pc.AddFile(RepairFolder.Windows, @"System32\FNTCACHE.DAT");
        _pc.AddFile(RepairFolder.Windows, @"System32\ntdll.dll");

        var result = await Run(RepairToolIds.IconAndFontCache);

        Assert.True(result.Succeeded, result.Summary);
        Assert.True(result.RestartNeeded);
        Assert.False(_pc.FileExists(RepairFolder.UserLocalAppData, "IconCache.db"));
        Assert.False(_pc.FileExists(RepairFolder.UserLocalAppData, @"Microsoft\Windows\Explorer\iconcache_48.db"));
        Assert.True(_pc.FileExists(RepairFolder.UserLocalAppData, @"Microsoft\Windows\Explorer\thumbcache_32.db"));
        Assert.False(_pc.FileExists(RepairFolder.Windows, @"ServiceProfiles\LocalService\AppData\Local\FontCache\Fonts\font.dat"));
        Assert.False(_pc.FileExists(RepairFolder.Windows, @"System32\FNTCACHE.DAT"));
        Assert.True(_pc.FileExists(RepairFolder.Windows, @"System32\ntdll.dll"));
        Assert.Contains("1 in use", result.Summary);
        Assert.Equal("net.exe start FontCache", _pc.Actions[^1]);
    }

    [Fact]
    public async Task Store_cache_reset_is_offered_only_when_the_Store_is_installed()
    {
        var missing = Tool(RepairToolIds.StoreCache);
        Assert.False(missing.Available);
        Assert.Contains("isn't installed", missing.NotAvailableReason);
        var refused = await Run(RepairToolIds.StoreCache);
        Assert.False(refused.Succeeded);
        Assert.Empty(_pc.Actions);

        _pc.AddFile(RepairFolder.UserLocalAppData, @"Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalCache\Local\cache.bin");
        _pc.AddFile(RepairFolder.UserLocalAppData, @"Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\Settings\settings.dat");
        Assert.True(Tool(RepairToolIds.StoreCache).Available);

        var result = await Run(RepairToolIds.StoreCache);

        Assert.True(result.Succeeded, result.Summary);
        Assert.False(_pc.FileExists(RepairFolder.UserLocalAppData, @"Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalCache\Local\cache.bin"));
        Assert.True(_pc.FileExists(RepairFolder.UserLocalAppData, @"Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\Settings\settings.dat"));
    }

    [Fact]
    public async Task Runtime_reinstall_covers_Visual_CPP_DirectX_and_NET_and_one_failure_does_not_stop_the_rest()
    {
        Assert.Equal(RestartNeed.Maybe, Tool(RepairToolIds.Runtimes).Restart);
        _pc.Script("winget.exe install --id Microsoft.VCRedist.2015+.x64", 3010);
        _pc.Script("winget.exe install --id Microsoft.DirectX", unchecked((int)0x8A150011), "No applicable installer found.");

        var result = await Run(RepairToolIds.Runtimes);

        var installed = _pc.Actions.Select(a => a.Split(' ')[3]).ToList();
        Assert.Contains("Microsoft.VCRedist.2015+.x64", installed);
        Assert.Contains("Microsoft.VCRedist.2015+.x86", installed);
        Assert.Contains("Microsoft.DirectX", installed);
        Assert.Equal("Microsoft.DotNet.DesktopRuntime.10", installed[^1]);
        Assert.All(_pc.Actions, a => Assert.Contains("--source winget", a));
        Assert.False(result.Succeeded);
        Assert.True(result.RestartNeeded);
        Assert.Contains("Couldn't reinstall DirectX legacy runtime (error 0x8A150011)", result.Summary);
    }

    [Fact]
    public async Task Stopping_a_repair_still_restarts_the_services_it_stopped()
    {
        _pc.Hang("net.exe stop bits");
        using var stop = new CancellationTokenSource();

        var result = await NewEngine().RunRepairAsync(
            RepairToolIds.WindowsUpdateReset, false, new StopWhen(p => p.StepName == "Stop bits", stop), stop.Token);

        Assert.False(result.Succeeded);
        Assert.StartsWith("Stopped.", result.Summary);
        Assert.DoesNotContain(_pc.Actions, a => a.StartsWith("clear") || a.StartsWith("net.exe stop cryptsvc"));
        Assert.Equal(["net.exe start cryptsvc", "net.exe start bits", "net.exe start wuauserv", "net.exe start UsoSvc"], _pc.Actions[^4..]);
        Assert.Single(_logs.Saved);
    }

    [Fact]
    public async Task A_run_whose_log_cannot_be_saved_still_reports_and_keeps_the_log_text_to_copy()
    {
        var engine = new Engine(
            Catalogue.Default,
            new SystemAdapters(new FakeRegistry(), new FakeSystemInfo(), Repair: _pc),
            new ChangeJournal(new InMemoryJournalStore()),
            "S-1-5-21-test",
            repairLogs: new FailingLogStore());

        var result = await engine.RunRepairAsync(RepairToolIds.NetworkRepair, false, cancellation: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Null(result.LogLocation);
        Assert.Contains("ipconfig.exe /flushdns", result.LogText);
        Assert.Contains("couldn't be saved", result.Summary);
    }

    [Fact]
    public async Task An_unknown_repair_tool_is_refused()
    {
        var result = await Run("repair.no-such-tool");

        Assert.False(result.Succeeded);
        Assert.Empty(_pc.Actions);
    }

    private sealed class FailingLogStore : IRepairLogStore
    {
        public string Save(string name, string text) => throw new IOException("Disk full.");
    }

    private sealed class StopWhen(Func<RepairProgress, bool> when, CancellationTokenSource stop) : IProgress<RepairProgress>
    {
        public void Report(RepairProgress value)
        {
            if (when(value))
                stop.Cancel();
        }
    }

    /// <summary>Records progress synchronously, as the pipe server does.</summary>
    private sealed class Collect(List<RepairProgress> into) : IProgress<RepairProgress>
    {
        public void Report(RepairProgress value) => into.Add(value);
    }
}
