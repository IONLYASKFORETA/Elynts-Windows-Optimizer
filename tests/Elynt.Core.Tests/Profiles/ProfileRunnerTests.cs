using Elynt.Core.Journal;
using Elynt.Core.Power;
using Elynt.Core.Profiles;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Time.Testing;

namespace Elynt.Core.Tests.Profiles;

/// <summary>Profiles following games as they start and close, driven by fake process events and a fake clock.</summary>
public class ProfileRunnerTests : IDisposable
{
    private const string User = "S-1-5-21-test";
    private const string Valorant = @"C:\Riot Games\VALORANT\live\VALORANT.exe";
    private const string Cs2 = @"C:\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe";

    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();
    private readonly FakePowerPlans _power = new();
    private readonly FakeProcesses _processes = new();
    private readonly FakeTimerResolution _timer = new();
    private readonly InMemoryJournalStore _journalStore = new();
    private readonly InMemoryProfileStore _profiles = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 18, 20, 0, 0, TimeSpan.Zero));
    private readonly FakeProcessEvents _events = new();
    private readonly ProfileNotes _notes = new();
    private readonly ChangeJournal _journal;
    private readonly CommandLane _lane = new();
    private readonly ProfileRunner _runner;

    public ProfileRunnerTests()
    {
        _journal = new ChangeJournal(_journalStore);
        _runner = new ProfileRunner(_events, EngineFor, _notes, _clock, lane: _lane);
        Save(Valorant, GamingTweaks.HighPerformancePlan.Id);
        Save(Cs2, GamingTweaks.UltimatePerformancePlan.Id);
    }

    public void Dispose() => _runner.Dispose();

    private Engine EngineFor(string user) => new(
        Catalogue.Default,
        new SystemAdapters(_registry, _system) { Power = _power, Processes = _processes, Timer = _timer },
        _journal,
        user,
        clock: _clock,
        profiles: _profiles);

    private void Save(string exe, string tweakId) =>
        Assert.True(EngineFor(User).SaveProfile(new GameProfile(exe) { TweakIds = [tweakId] }).Succeeded);

    private void Launch(string exe, int pid)
    {
        _processes.Run(exe, pid);
        _events.Start(pid, exe, User);
    }

    private void Close(int pid)
    {
        _processes.Exit(pid);
        _events.Exit(pid);
    }

    [Fact]
    public void The_profile_applies_when_the_game_starts_and_is_undone_15_seconds_after_it_closes()
    {
        Launch(Valorant, 1);
        Assert.Equal(PowerPlans.HighPerformance, _power.Active);

        Close(1);
        _clock.Advance(TimeSpan.FromSeconds(14));
        Assert.Equal(PowerPlans.HighPerformance, _power.Active);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(PowerPlans.Balanced, _power.Active);
    }

    [Fact]
    public void A_game_that_restarts_within_the_grace_period_keeps_its_profile_without_flicker()
    {
        Launch(Valorant, 1);
        Close(1);
        _clock.Advance(TimeSpan.FromSeconds(10));

        Launch(Valorant, 2);
        _clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(PowerPlans.HighPerformance, _power.Active);
        Assert.Single(_journal.EntriesFor(User));
        Close(2);
        _clock.Advance(ProfileRunner.Grace);
        Assert.Equal(PowerPlans.Balanced, _power.Active);
    }

    [Fact]
    public void The_profile_stays_while_any_copy_of_the_game_still_runs()
    {
        Launch(Valorant, 1);
        Launch(Valorant, 2);

        Close(1);
        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(PowerPlans.HighPerformance, _power.Active);
    }

    [Fact]
    public void The_first_running_game_wins_and_a_note_says_the_second_was_skipped()
    {
        Launch(Valorant, 1);
        _notes.Take(User);

        Launch(Cs2, 2);

        Assert.Equal(PowerPlans.HighPerformance, _power.Active);
        var note = Assert.Single(_notes.Take(User));
        Assert.Equal(ProfileNoteKind.Skipped, note.Kind);
        Assert.Contains("VALORANT", note.Message);
        Assert.Contains("cs2", note.Message);
    }

    [Fact]
    public void A_second_game_started_during_the_first_one_s_grace_period_is_skipped_too()
    {
        Launch(Valorant, 1);
        Close(1);
        Launch(Cs2, 2);

        Assert.Equal(ProfileNoteKind.Skipped, _notes.Take(User).Last().Kind);
        _clock.Advance(ProfileRunner.Grace);
        Assert.Equal(PowerPlans.Balanced, _power.Active);
    }

    [Fact]
    public void After_the_first_game_s_profile_ends_the_next_game_to_start_gets_its_own()
    {
        Launch(Valorant, 1);
        Close(1);
        _clock.Advance(ProfileRunner.Grace);

        Launch(Cs2, 2);

        Assert.Equal(PowerPlans.ElyntUltimatePerformance, _power.Active);
    }

    [Fact]
    public void Quiet_notes_say_when_a_profile_applied_and_when_it_was_undone()
    {
        Launch(Valorant, 1);
        Close(1);
        _clock.Advance(ProfileRunner.Grace);

        Assert.Equal([ProfileNoteKind.Applied, ProfileNoteKind.Reverted], _notes.Take(User).Select(n => n.Kind));
        Assert.Empty(_notes.Take(User));
    }

    [Fact]
    public void Programs_without_a_profile_change_nothing()
    {
        Launch(@"C:\Program Files\Notepad++\notepad++.exe", 1);
        Close(1);
        _clock.Advance(ProfileRunner.Grace);

        Assert.Empty(_journal.EntriesFor(User));
        Assert.Empty(_notes.Take(User));
    }

    [Fact]
    public void Another_user_s_game_doesn_t_use_this_user_s_profile()
    {
        _processes.Run(Valorant, 1);
        _events.Start(1, Valorant, "S-1-5-21-someone-else");

        Assert.Equal(PowerPlans.Balanced, _power.Active);
    }

    [Fact]
    public void Leftovers_from_a_crash_are_undone_when_the_runner_recovers_at_service_start()
    {
        Launch(Valorant, 1);
        // The service dies mid-game: a new runner over the same journal file.
        _processes.Exit(1);
        using var restarted = new ProfileRunner(new FakeProcessEvents(), EngineFor, _notes, _clock);

        restarted.RecoverLeftovers(new ChangeJournal(_journalStore).UsersWithTemporaryChanges());

        Assert.Equal(PowerPlans.Balanced, _power.Active);
    }

    [Fact]
    public async Task A_profile_waits_for_the_command_in_progress_so_the_two_never_interleave()
    {
        var command = _lane.Enter();
        var launch = Task.Run(() => Launch(Valorant, 1), TestContext.Current.CancellationToken);

        var first = await Task.WhenAny(launch, Task.Delay(300, TestContext.Current.CancellationToken));
        Assert.NotSame(launch, first);
        Assert.Equal(PowerPlans.Balanced, _power.Active);

        command.Dispose();
        await launch.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(PowerPlans.HighPerformance, _power.Active);
    }
}

public sealed class FakeProcessEvents : IProcessEvents
{
    public event EventHandler<ProcessStarted>? Started;

    public event EventHandler<int>? Exited;

    public void Start(int pid, string exePath, string user) => Started?.Invoke(this, new ProcessStarted(pid, exePath, user));

    public void Exit(int pid) => Exited?.Invoke(this, pid);
}
