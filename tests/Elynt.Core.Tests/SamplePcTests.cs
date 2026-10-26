using Elynt.Core.Drift;
using Elynt.Core.Games;

namespace Elynt.Core.Tests;

/// <summary>
/// The screenshot tour only draws a row template when its list has rows, so a binding error in an empty list never
/// fails CI (the Drivers page carried one until the sample PC got a GPU). These keep every list on the sample PC
/// filled, with at least two rows, and every state the tour shows reachable.
/// </summary>
public sealed class SamplePcTests : IDisposable
{
    private readonly string _scratch = Directory.CreateTempSubdirectory("elynt-sample-pc-").FullName;

    public void Dispose() => Directory.Delete(_scratch, recursive: true);

    private Engine NewEngine() => SamplePc.NewEngine(_scratch);

    [Fact]
    public void A_tweak_Windows_set_by_itself_is_already_set_so_the_tour_draws_its_locked_switch()
    {
        var tweak = NewEngine().ListTweaks().Single(t => t.Id == SamplePc.SetByWindows);

        Assert.Equal((TweakState.On, true), (tweak.State, tweak.AlreadySet));
    }

    [Fact]
    public void Startup_and_services_have_rows()
    {
        var engine = NewEngine();

        Assert.True(engine.ListStartupItems().Count >= 2);
        Assert.True(engine.ListServices().Count >= 2);
    }

    [Fact]
    public void Games_include_one_added_by_its_exe_and_one_with_a_profile()
    {
        var engine = NewEngine();

        var games = engine.ListGames();
        Assert.True(games.Count >= 2);
        Assert.Contains(games, g => g.Launcher == GameLauncher.Manual);
        var profile = Assert.Single(engine.ListProfiles());
        Assert.True(profile.AppsToClose.Count >= 2);
        Assert.Contains(games, g => g.ExePath == profile.ExePath);
    }

    [Fact]
    public void The_undo_history_has_an_undone_row_and_drift_whose_undo_is_refused()
    {
        var engine = NewEngine();

        var history = engine.ListHistory();
        Assert.True(history.Count >= 2);
        Assert.Contains(history, c => c.TweakId == SamplePc.Undone && c.State == ChangeState.Undone);
        Assert.Contains(engine.CheckDrift().Drifted, d => d.TweakId == SamplePc.Drifted);

        var drifted = history.First(c => c.TweakId == SamplePc.Drifted && c.CanUndo);
        Assert.False(engine.UndoChange(drifted.Id).Succeeded);
    }

    [Fact]
    public void Drift_has_two_rows_one_of_them_a_startup_item_switched_back_on()
    {
        var drifted = NewEngine().CheckDrift().Drifted;

        Assert.True(drifted.Count >= 2);
        Assert.Contains(drifted, d => d.Kind == DriftKind.StartupItemSwitchedBack && d.TweakId == $"startup:{SamplePc.DriftedStartupItem}");
    }

    [Fact]
    public async Task A_cleanup_scan_finds_something_in_several_categories()
    {
        var scan = await NewEngine().ScanCleanupAsync(cancellation: TestContext.Current.CancellationToken);

        Assert.True(scan.Categories.Count(c => c.SizeBytes > 0) >= 2);
        Assert.Contains(scan.Categories, c => c.SizeBytes > 0 && c.NeedsConfirmation);
    }

    [Fact]
    public void Drivers_have_problem_drivers_and_a_backup()
    {
        var cleaning = NewEngine().GetDriverCleaning();

        Assert.True(cleaning.ProblemDrivers.Count >= 2);
        Assert.NotEmpty(cleaning.Backups);
    }

    [Fact]
    public void Overclocking_has_sliders_for_the_gpu_and_the_cpu()
    {
        var engine = NewEngine();

        var gpu = Assert.Single(engine.GetGpuTuning().Gpus);
        Assert.True(gpu.Controls.Count >= 2);
        var cpu = engine.GetCpuTuning(advanced: false);
        Assert.True(cpu.Available, cpu.Notice);
        Assert.True(cpu.Controls.Count >= 2);
    }

    [Fact]
    public void Apps_have_installed_apps_with_updates_and_search_results()
    {
        var engine = NewEngine();

        var installed = engine.ListInstalledApps();
        Assert.True(installed.Apps.Count >= 2);
        Assert.Contains(installed.Apps, a => a.CanUpdate);
        Assert.True(engine.SearchApps(SamplePc.AppSearch).Results.Count >= 2);
    }
}
