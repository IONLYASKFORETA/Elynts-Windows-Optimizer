using Elynt.Core.Journal;
using Elynt.Core.Tweaks;
using Elynt.Core.UpdateControl;

namespace Elynt.Core.Tests.WindowsUpdate;

/// <summary>"Check and install now", driven through the Engine with a fake Windows Update agent.</summary>
public class CheckAndInstallNowTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new() { OsBuild = "26100.4652" };
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());
    private readonly FakeUpdateAgent _agent = new();
    private readonly ManualUpdate _updates;

    public CheckAndInstallNowTests()
    {
        // Runs the update inline, so a test sees the finished result when Start returns.
        _updates = new ManualUpdate(_agent, run => { run(); return Task.CompletedTask; });
    }

    private Engine NewEngine() =>
        new(Catalogue.Default, new SystemAdapters(_registry, _system), _journal, "S-1-5-21-test", windowsUpdate: _updates);

    private static readonly AvailableUpdate Cumulative = new("kb1", "2026-10 Cumulative Update for Windows 11", UpdateKind.Quality);
    private static readonly AvailableUpdate Defender = new("kb2", "Security Intelligence Update for Microsoft Defender", UpdateKind.Quality);
    private static readonly AvailableUpdate GpuDriver = new("drv", "NVIDIA - Display - 32.0.15.6094", UpdateKind.Driver);
    private static readonly AvailableUpdate FeatureUpdate = new("fu", "Windows 11, version 25H2", UpdateKind.Feature);

    [Fact]
    public void Installs_quality_updates_one_at_a_time_and_reports_progress()
    {
        _agent.Available = [Cumulative, Defender];
        var engine = NewEngine();
        _agent.OnStep = step => _agent.Seen.Add((step, engine.GetWindowsUpdateStatus()));

        var started = engine.StartWindowsUpdate();

        Assert.Equal(
            [
                ("download kb1", UpdatePhase.Downloading, 0, 2, Cumulative.Title, 0),
                ("download kb2", UpdatePhase.Downloading, 1, 2, Defender.Title, 25),
                ("install kb1", UpdatePhase.Installing, 0, 2, Cumulative.Title, 50),
                ("install kb2", UpdatePhase.Installing, 1, 2, Defender.Title, 75),
            ],
            _agent.Seen.Select(s => (s.Step, s.Status.Phase, s.Status.Done, s.Status.Total, s.Status.Current, s.Status.Percent)));
        var done = engine.GetWindowsUpdateStatus();
        Assert.Equal(started, done);
        Assert.Equal(UpdatePhase.Finished, done.Phase);
        Assert.Equal(100, done.Percent);
        Assert.Equal([Cumulative.Title, Defender.Title], done.Installed);
        Assert.False(done.RebootRequired);
    }

    [Fact]
    public void Drivers_and_feature_updates_are_left_alone()
    {
        _agent.Available = [GpuDriver, Cumulative, FeatureUpdate];

        var done = NewEngine().StartWindowsUpdate();

        Assert.Equal(["install kb1"], _agent.Steps.Where(s => s.StartsWith("install")));
        Assert.Equal([Cumulative.Title], done.Installed);
        Assert.Equal([GpuDriver.Title, FeatureUpdate.Title], done.Skipped);
        Assert.Contains("drivers and feature updates", done.Message);
    }

    [Fact]
    public void Says_when_a_restart_is_needed()
    {
        _agent.Available = [Cumulative];
        _agent.NeedsRestart.Add("kb1");

        var done = NewEngine().StartWindowsUpdate();

        Assert.True(done.RebootRequired);
        Assert.Contains("Restart", done.Message);
    }

    [Fact]
    public void One_failed_update_does_not_stop_the_rest()
    {
        _agent.Available = [Cumulative, Defender];
        _agent.FailInstall.Add("kb1");

        var done = NewEngine().StartWindowsUpdate();

        Assert.Equal(UpdatePhase.Finished, done.Phase);
        Assert.Equal([Defender.Title], done.Installed);
        Assert.Equal([Cumulative.Title], done.NotInstalled);
        Assert.Contains("1 of 2", done.Message);
    }

    [Fact]
    public void A_failed_check_is_reported()
    {
        _agent.SearchError = new InvalidOperationException("0x8024402C");

        var done = NewEngine().StartWindowsUpdate();

        Assert.Equal(UpdatePhase.Failed, done.Phase);
        Assert.Contains("0x8024402C", done.Message);
    }

    [Fact]
    public void Nothing_to_install_says_up_to_date()
    {
        var done = NewEngine().StartWindowsUpdate();

        Assert.Equal(UpdatePhase.Finished, done.Phase);
        Assert.Contains("up to date", done.Message);
    }

    [Fact]
    public void Works_while_updates_are_paused()
    {
        _agent.Available = [Cumulative];
        var engine = NewEngine();
        Assert.True(engine.Apply("update.pause").Succeeded);

        var done = engine.StartWindowsUpdate();

        Assert.Equal([Cumulative.Title], done.Installed);
        Assert.Equal(TweakState.On, engine.ListTweaks().Single(t => t.Id == "update.pause").State);
    }

    [Fact]
    public void Starting_again_while_running_shows_the_running_update_instead_of_starting_another()
    {
        _agent.Available = [Cumulative];
        var engine = NewEngine();
        UpdateRunStatus? second = null;
        _agent.OnStep = step => second ??= engine.StartWindowsUpdate();

        engine.StartWindowsUpdate();

        Assert.Equal(UpdatePhase.Downloading, second!.Phase);
        Assert.Equal(1, _agent.Searches);
    }

    [Fact]
    public void Before_anything_runs_the_status_is_idle()
    {
        Assert.Equal(UpdatePhase.Idle, NewEngine().GetWindowsUpdateStatus().Phase);
    }

    [Fact]
    public void Without_a_Windows_Update_agent_the_command_fails_cleanly()
    {
        var engine = new Engine(Catalogue.Default, new SystemAdapters(_registry, _system), _journal, "S-1-5-21-test");

        var result = engine.StartWindowsUpdate();

        Assert.Equal(UpdatePhase.Failed, result.Phase);
    }

    private sealed class FakeUpdateAgent : IWindowsUpdateAgent
    {
        public IReadOnlyList<AvailableUpdate> Available { get; set; } = [];
        public HashSet<string> FailInstall { get; } = [];
        public HashSet<string> NeedsRestart { get; } = [];
        public Exception? SearchError { get; set; }
        public List<string> Steps { get; } = [];
        public List<(string Step, UpdateRunStatus Status)> Seen { get; } = [];
        public Action<string>? OnStep { get; set; }
        public int Searches { get; private set; }

        public IReadOnlyList<AvailableUpdate> Search()
        {
            Searches++;
            if (SearchError is not null)
                throw SearchError;
            return Available;
        }

        public void Download(AvailableUpdate update) => Step($"download {update.Id}");

        public InstallOutcome Install(AvailableUpdate update)
        {
            Step($"install {update.Id}");
            return FailInstall.Contains(update.Id)
                ? new InstallOutcome(false, false, "0x80070643")
                : new InstallOutcome(true, NeedsRestart.Contains(update.Id));
        }

        private void Step(string step)
        {
            Steps.Add(step);
            OnStep?.Invoke(step);
        }
    }
}
