using Elynt.Core.Journal;
using Elynt.Core.Power;
using Elynt.Core.Profiles;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests.Profiles;

/// <summary>Building per-game profiles through the Engine: only instant changes may go in.</summary>
public class ProfileTests
{
    private const string User = "S-1-5-21-test";
    private const string Game = @"C:\Games\Valorant\VALORANT.exe";

    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();
    private readonly FakePowerPlans _power = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());
    private readonly InMemoryProfileStore _profiles = new();
    private readonly FakeProcesses _processes = new();
    private readonly FakeTimerResolution _timer = new();

    // The real Defender-off tweak, which is in Security and so must never be allowed in a per-game profile.
    private static readonly TweakDefinition Defender = SecurityTweaks.DefenderOff;

    private Engine NewEngine(ChangeJournal? journal = null) => new(
        new Catalogue([.. Catalogue.Default.Tweaks.Where(t => !t.PerGame)], GamingTweaks.PerGame),
        new SystemAdapters(_registry, _system) { Power = _power, Processes = _processes, Timer = _timer },
        journal ?? _journal,
        User,
        profiles: _profiles);

    [Fact]
    public void A_profile_of_instant_changes_is_saved_for_the_game()
    {
        var engine = NewEngine();

        var result = engine.SaveProfile(new GameProfile(Game)
        {
            TweakIds = [GamingTweaks.GameModeOn.Id, GamingTweaks.HighPerformancePlan.Id],
            AppsToClose = [@"C:\Users\Aden\AppData\Local\Discord\Discord.exe"],
            Priority = GamePriority.High,
            RaiseTimerResolution = true,
        });

        Assert.True(result.Succeeded, result.Message);
        var saved = Assert.Single(NewEngine().ListProfiles());
        Assert.Equal(Game, saved.ExePath);
        Assert.Equal([GamingTweaks.GameModeOn.Id, GamingTweaks.HighPerformancePlan.Id], saved.TweakIds);
        Assert.Equal([@"C:\Users\Aden\AppData\Local\Discord\Discord.exe"], saved.AppsToClose);
        Assert.Equal(GamePriority.High, saved.Priority);
        Assert.True(saved.RaiseTimerResolution);
    }

    [Fact]
    public void The_Engine_rejects_a_reboot_only_tweak_in_a_profile()
    {
        var result = NewEngine().SaveProfile(new GameProfile(Game) { TweakIds = [GamingTweaks.HardwareGpuSchedulingOn.Id] });

        Assert.False(result.Succeeded);
        Assert.Contains("reboot", result.Message);
        Assert.Empty(NewEngine().ListProfiles());
    }

    [Fact]
    public void The_Engine_rejects_Defender_in_a_profile()
    {
        var result = NewEngine().SaveProfile(new GameProfile(Game) { TweakIds = [GamingTweaks.GameModeOn.Id, Defender.Id] });

        Assert.False(result.Succeeded);
        Assert.Contains("Defender", result.Message);
        Assert.Empty(NewEngine().ListProfiles());
    }

    private const string Discord = @"C:\Users\Aden\AppData\Local\Discord\Discord.exe";
    private static readonly RegistryLocation GameMode = new(RegistryRoot.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled");

    private Engine EngineWithProfile(ChangeJournal? journal = null)
    {
        var engine = NewEngine(journal);
        var saved = engine.SaveProfile(new GameProfile(Game)
        {
            TweakIds = [GamingTweaks.GameModeOn.Id, GamingTweaks.HighPerformancePlan.Id],
            AppsToClose = [Discord],
            Priority = GamePriority.High,
            RaiseTimerResolution = true,
        });
        Assert.True(saved.Succeeded, saved.Message);
        return engine;
    }

    [Fact]
    public void Starting_a_profile_makes_its_changes_as_temporary_ones()
    {
        _processes.Run(Discord, 100);
        _processes.Run(Game, 4242);

        var result = EngineWithProfile().StartProfile(Game, 4242);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(PowerPlans.HighPerformance, _power.Active);
        Assert.Equal(RegistryValue.DWord(1), _registry.GetValue(GameMode));
        Assert.False(_processes.IsRunning(Discord));
        Assert.Equal(GamePriority.High, _processes.PriorityOf(4242));
        Assert.True(_timer.Raised);
        Assert.NotEmpty(_journal.EntriesFor(User));
        Assert.All(_journal.EntriesFor(User), entry => Assert.True(entry.Temporary));
    }

    [Fact]
    public void Stopping_a_profile_puts_everything_back_and_reopens_closed_apps()
    {
        _processes.Run(Discord, 100);
        _processes.Run(Game, 4242);
        var engine = EngineWithProfile();
        engine.StartProfile(Game, 4242);
        _processes.Exit(4242);

        var result = engine.StopProfile(Game);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(PowerPlans.Balanced, _power.Active);
        Assert.Null(_registry.GetValue(GameMode));
        Assert.True(_processes.IsRunning(Discord));
        Assert.Equal([(Discord, User)], _processes.Started);
        Assert.False(_timer.Raised);
        Assert.Empty(_journal.UndoableTweakIds(User));
    }

    [Fact]
    public void Apps_that_weren_t_running_are_not_opened_afterwards()
    {
        _processes.Run(Game, 4242);
        var engine = EngineWithProfile();

        engine.StartProfile(Game, 4242);
        engine.StopProfile(Game);

        Assert.Empty(_processes.Started);
    }

    [Fact]
    public void Temporary_changes_left_by_a_crash_or_reboot_are_undone_when_the_service_starts_again()
    {
        var store = new InMemoryJournalStore();
        _processes.Run(Discord, 100).Run(Game, 4242);
        EngineWithProfile(new ChangeJournal(store)).StartProfile(Game, 4242);
        // A global setting the user chose stays.
        Assert.True(NewEngine(new ChangeJournal(store)).Apply(GamingTweaks.WindowedGamesOptimizationsOn.Id).Succeeded);

        // The service dies mid-game; the game and Discord are gone after the reboot.
        _processes.Exit(4242);
        var restarted = new ChangeJournal(store);
        Assert.Equal([User], restarted.UsersWithTemporaryChanges());

        var result = NewEngine(restarted).RecoverTemporaryChanges();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(PowerPlans.Balanced, _power.Active);
        Assert.Null(_registry.GetValue(GameMode));
        Assert.True(_processes.IsRunning(Discord));
        Assert.Empty(restarted.UsersWithTemporaryChanges());
        Assert.Equal([GamingTweaks.WindowedGamesOptimizationsOn.Id], restarted.UndoableTweakIds(User));
    }

    [Fact]
    public void Recovery_with_nothing_left_behind_changes_nothing()
    {
        var result = NewEngine().RecoverTemporaryChanges();

        Assert.True(result.Succeeded);
        Assert.Empty(_journal.EntriesFor(User));
    }

    [Fact]
    public void A_profile_can_t_close_Windows_itself_or_its_own_game()
    {
        Assert.False(NewEngine().SaveProfile(new GameProfile(Game) { AppsToClose = [@"C:\Windows\explorer.exe"] }).Succeeded);
        Assert.False(NewEngine().SaveProfile(new GameProfile(Game) { AppsToClose = [Game] }).Succeeded);
    }

    [Fact]
    public void Settings_that_were_already_as_the_profile_wants_are_left_alone_afterwards()
    {
        _power.Active = PowerPlans.HighPerformance;
        _processes.Run(Game, 4242);
        var engine = EngineWithProfile();

        engine.StartProfile(Game, 4242);
        engine.StopProfile(Game);

        Assert.Equal(PowerPlans.HighPerformance, _power.Active);
    }

    [Fact]
    public void A_profile_change_re_applied_after_drift_stays_temporary_and_is_undone_when_the_game_closes()
    {
        _processes.Run(Game, 4242);
        var engine = EngineWithProfile();
        engine.StartProfile(Game, 4242);
        // Something else turns Game Mode off mid-game; the user re-applies it from the drift list.
        _registry.SetValue(GameMode, RegistryValue.DWord(0));

        var reapplied = engine.ReapplyDrift([GamingTweaks.GameModeOn.Id]);

        Assert.True(reapplied.Succeeded, reapplied.Message);
        Assert.True(_journal.LatestUndoable(GamingTweaks.GameModeOn.Id, User)!.Temporary);
        engine.StopProfile(Game);
        Assert.Equal(RegistryValue.DWord(0), _registry.GetValue(GameMode));
        Assert.Empty(_journal.OpenTemporary(User));
    }

    [Fact]
    public void A_tweak_the_user_turns_on_while_the_profile_has_it_on_stays_on_after_the_game_closes()
    {
        _processes.Run(Game, 4242);
        var engine = EngineWithProfile();
        engine.StartProfile(Game, 4242);

        var applied = engine.Apply(GamingTweaks.GameModeOn.Id);
        engine.StopProfile(Game);

        Assert.True(applied.Succeeded, applied.Message);
        Assert.Equal(RegistryValue.DWord(1), _registry.GetValue(GameMode));
        Assert.Equal(TweakState.On, engine.ListTweaks().Single(t => t.Id == GamingTweaks.GameModeOn.Id).State);
        // Undo still goes back to how it was before the game.
        Assert.True(engine.Revert(GamingTweaks.GameModeOn.Id).Succeeded);
        Assert.Null(_registry.GetValue(GameMode));
    }
}
