using Elynt.Core.Journal;
using Elynt.Core.Power;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>The Gaming section's proven and situational tweaks, driven through the Engine.</summary>
public class GamingTweakTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();
    private readonly FakeBootConfig _boot = new();
    private readonly FakePowerPlans _power = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    private Engine NewEngine() => new(Catalogue.Default, new SystemAdapters(_registry, _system) { Boot = _boot, Power = _power }, _journal, "S-1-5-21-test");

    private TweakView View(string id) => NewEngine().ListTweaks().Single(t => t.Id == id);

    [Fact]
    public void Game_Mode_on_sets_the_Game_Bar_switch_and_can_go_in_a_per_game_profile()
    {
        var engine = NewEngine();

        var result = engine.Apply(GamingTweaks.GameModeOn.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(RegistryValue.DWord(1),
            _registry.GetValue(new(RegistryRoot.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled")));
        Assert.Equal(EvidenceTier.Situational, result.Tweak!.Tier);
        Assert.True(result.Tweak.ProfileEligible);
    }

    [Fact]
    public void A_dual_CCD_X3D_CPU_gets_the_core_parking_warning_on_Game_Mode()
    {
        _system.Hardware = HardwareTraits.AmdCpu | HardwareTraits.DualCcdX3dCpu;

        var warning = Assert.Single(View(GamingTweaks.GameModeOn.Id).Warnings);
        Assert.Contains("X3D", warning);
        Assert.Contains("Balanced", warning);
    }

    [Fact]
    public void Other_CPUs_get_no_X3D_warning()
    {
        _system.Hardware = HardwareTraits.AmdCpu;

        Assert.Empty(View(GamingTweaks.GameModeOn.Id).Warnings);
    }

    [Fact]
    public void HAGS_turns_on_GPU_scheduling_and_waits_for_a_reboot()
    {
        var result = NewEngine().Apply(GamingTweaks.HardwareGpuSchedulingOn.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(RegistryValue.DWord(2), _registry.GetValue(
            new(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode")));
        Assert.True(result.Tweak!.Risks.HasFlag(RiskFlags.NeedsReboot));
        Assert.True(result.Tweak.PendingReboot);
        Assert.False(result.Tweak.ProfileEligible);
    }

    [Fact]
    public void An_RTX_40_or_50_card_is_told_Frame_Generation_needs_HAGS()
    {
        _system.Hardware = HardwareTraits.NvidiaGpu | HardwareTraits.FrameGenerationGpu;

        var warning = Assert.Single(View(GamingTweaks.HardwareGpuSchedulingOn.Id).Warnings);
        Assert.Contains("Frame Generation", warning);
    }

    [Fact]
    public void Older_cards_get_no_Frame_Generation_note()
    {
        _system.Hardware = HardwareTraits.NvidiaGpu;

        Assert.Empty(View(GamingTweaks.HardwareGpuSchedulingOn.Id).Warnings);
    }

    [Fact]
    public void Global_timer_resolution_makes_timer_requests_system_wide_after_a_reboot()
    {
        var result = NewEngine().Apply(GamingTweaks.GlobalTimerResolution.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(RegistryValue.DWord(1), _registry.GetValue(
            new(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests")));
        Assert.Equal(EvidenceTier.Situational, result.Tweak!.Tier);
        Assert.True(result.Tweak.Risks.HasFlag(RiskFlags.NeedsReboot));
    }

    private static readonly RegistryLocation DirectXSettings =
        new(RegistryRoot.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings");

    [Fact]
    public void Windowed_games_optimizations_are_added_without_touching_the_other_DirectX_settings()
    {
        _registry.SetValue(DirectXSettings, RegistryValue.String("VRROptimizeEnable=0;AutoHDREnable=1;"));

        var result = NewEngine().Apply(GamingTweaks.WindowedGamesOptimizationsOn.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(TweakState.On, result.Tweak!.State);
        Assert.Equal(RegistryValue.String("VRROptimizeEnable=0;AutoHDREnable=1;SwapEffectUpgradeEnable=1;"), _registry.GetValue(DirectXSettings));
        Assert.Equal(EvidenceTier.Proven, result.Tweak.Tier);
    }

    [Fact]
    public void Windowed_games_optimizations_switched_off_in_Settings_are_switched_back_on_in_place()
    {
        _registry.SetValue(DirectXSettings, RegistryValue.String("SwapEffectUpgradeEnable=0;VRROptimizeEnable=1;"));
        Assert.Equal(TweakState.Off, View(GamingTweaks.WindowedGamesOptimizationsOn.Id).State);

        NewEngine().Apply(GamingTweaks.WindowedGamesOptimizationsOn.Id);

        Assert.Equal(RegistryValue.String("SwapEffectUpgradeEnable=1;VRROptimizeEnable=1;"), _registry.GetValue(DirectXSettings));
    }

    [Fact]
    public void Undo_keeps_a_DirectX_setting_the_user_changed_since_and_only_puts_back_its_own()
    {
        _registry.SetValue(DirectXSettings, RegistryValue.String("SwapEffectUpgradeEnable=0;"));
        var engine = NewEngine();
        engine.Apply(GamingTweaks.WindowedGamesOptimizationsOn.Id);

        // The user turns on Auto HDR in Settings afterwards.
        _registry.SetValue(DirectXSettings, RegistryValue.String("SwapEffectUpgradeEnable=1;AutoHDREnable=1;"));
        var reverted = engine.Revert(GamingTweaks.WindowedGamesOptimizationsOn.Id);

        Assert.True(reverted.Succeeded, reverted.Message);
        Assert.Equal(RegistryValue.String("SwapEffectUpgradeEnable=0;AutoHDREnable=1;"), _registry.GetValue(DirectXSettings));
    }

    [Fact]
    public void A_forced_useplatformclock_is_removed_and_put_back_on_undo()
    {
        _boot.Set("useplatformclock", "Yes");
        var engine = NewEngine();
        Assert.Equal(TweakState.Off, View(GamingTweaks.UsePlatformClockRemoved.Id).State);

        var applied = engine.Apply(GamingTweaks.UsePlatformClockRemoved.Id);

        Assert.True(applied.Succeeded, applied.Message);
        Assert.Null(_boot.Get("useplatformclock"));
        Assert.Equal(TweakState.On, applied.Tweak!.State);
        Assert.True(applied.Tweak.PendingReboot);
        Assert.Equal(EvidenceTier.Proven, applied.Tweak.Tier);

        Assert.True(engine.Revert(GamingTweaks.UsePlatformClockRemoved.Id).Succeeded);
        Assert.Equal("Yes", _boot.Get("useplatformclock"));
    }

    [Fact]
    public void Without_a_forced_useplatformclock_the_tweak_already_shows_on_with_nothing_to_undo()
    {
        var view = View(GamingTweaks.UsePlatformClockRemoved.Id);

        Assert.Equal(TweakState.On, view.State);
        Assert.False(view.CanUndo);
    }

    [Fact]
    public void The_High_performance_plan_is_activated_and_undo_reactivates_the_previous_plan()
    {
        _power.Active = PowerPlans.Balanced;
        var engine = NewEngine();

        var applied = engine.Apply(GamingTweaks.HighPerformancePlan.Id);

        Assert.True(applied.Succeeded, applied.Message);
        Assert.Equal(PowerPlans.HighPerformance, _power.Active);
        Assert.True(applied.Tweak!.ProfileEligible);
        Assert.Equal(EvidenceTier.Situational, applied.Tweak.Tier);

        engine.Revert(GamingTweaks.HighPerformancePlan.Id);
        Assert.Equal(PowerPlans.Balanced, _power.Active);
    }

    [Fact]
    public void A_PC_without_the_High_performance_plan_is_left_as_it_was()
    {
        _power.Remove(PowerPlans.HighPerformance);
        _power.Active = PowerPlans.Balanced;

        var result = NewEngine().Apply(GamingTweaks.HighPerformancePlan.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(PowerPlans.Balanced, _power.Active);
        Assert.False(result.Tweak!.CanUndo);
    }

    [Fact]
    public void The_Ultimate_Performance_plan_is_created_for_the_app_and_deleted_again_on_undo()
    {
        _power.Active = PowerPlans.Balanced;
        var plansBefore = _power.Plans.ToList();
        var engine = NewEngine();

        var applied = engine.Apply(GamingTweaks.UltimatePerformancePlan.Id);

        Assert.True(applied.Succeeded, applied.Message);
        Assert.Equal(PowerPlans.ElyntUltimatePerformance, _power.Active);
        Assert.Contains(PowerPlans.ElyntUltimatePerformance, _power.Plans);

        engine.Revert(GamingTweaks.UltimatePerformancePlan.Id);
        Assert.Equal(PowerPlans.Balanced, _power.Active);
        Assert.Equal(plansBefore, _power.Plans);
    }

    [Theory]
    [InlineData("gaming.high-performance-plan")]
    [InlineData("gaming.ultimate-performance-plan")]
    [InlineData("gaming.game-mode-on")]
    public void Every_tweak_that_breaks_X3D_core_parking_warns_on_a_dual_CCD_X3D(string id)
    {
        _system.Hardware = HardwareTraits.AmdCpu | HardwareTraits.DualCcdX3dCpu;

        Assert.Contains(GamingTweaks.X3dCoreParking.Text, View(id).Warnings);
    }

    private const string Game = @"C:\Games\Example\example.exe";

    private static readonly RegistryLocation GameLayers =
        new(RegistryRoot.CurrentUser, @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", Game);

    private static string ForGame(string exe) => GamingTweaks.FullscreenOptimizationsOff.IdFor(exe);

    [Fact]
    public void Fullscreen_optimizations_are_listed_as_a_per_game_choice_not_a_global_switch()
    {
        var view = View(GamingTweaks.FullscreenOptimizationsOff.Id);

        Assert.True(view.PerGame);
        Assert.Equal(TweakState.Off, view.State);
        Assert.Equal(EvidenceTier.Situational, view.Tier);
    }

    [Fact]
    public void Fullscreen_optimizations_are_turned_off_for_one_game_keeping_its_other_compatibility_flags()
    {
        _registry.SetValue(GameLayers, RegistryValue.String("~ RUNASADMIN"));
        var engine = NewEngine();

        var result = engine.Apply(ForGame(Game));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(RegistryValue.String("~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE"), _registry.GetValue(GameLayers));
        Assert.Contains("example.exe", result.Tweak!.Name);
        Assert.False(result.Tweak.PerGame);
    }

    [Fact]
    public void A_game_with_fullscreen_optimizations_off_is_listed_until_it_is_undone()
    {
        var engine = NewEngine();
        engine.Apply(ForGame(Game));

        Assert.Contains(engine.ListTweaks(), t => t.Id == ForGame(Game) && t.State == TweakState.On && t.CanUndo);

        Assert.True(engine.Revert(ForGame(Game)).Succeeded);
        Assert.Null(_registry.GetValue(GameLayers));
        Assert.DoesNotContain(engine.ListTweaks(), t => t.Id == ForGame(Game));
    }

    [Theory]
    [InlineData(@"example.exe")]
    [InlineData(@"C:\Games\Example\readme.txt")]
    [InlineData(@"C:\Games\..\Windows\notepad")]
    public void Only_a_full_path_to_a_program_is_accepted(string exe)
    {
        var result = NewEngine().Apply(ForGame(exe));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void The_per_game_choice_itself_needs_a_game_before_it_can_be_applied()
    {
        var result = NewEngine().Apply(GamingTweaks.FullscreenOptimizationsOff.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("game", result.Message);
    }

    [Theory]
    [InlineData("gaming.useplatformclock-removed")]
    [InlineData("gaming.ultimate-performance-plan")]
    [InlineData("gaming.windowed-games-optimizations-on")]
    [InlineData(@"gaming.fullscreen-optimizations-off|C:\Games\Example\example.exe")]
    public void Undo_still_works_after_the_service_restarts(string id)
    {
        _boot.Set("useplatformclock", "Yes");
        _registry.SetValue(DirectXSettings, RegistryValue.String("AutoHDREnable=1;"));
        var tweak = Catalogue.Default.Get(id);
        var adapters = new SystemAdapters(_registry, _system) { Boot = _boot, Power = _power };
        var before = Elynt.Tests.Shared.SystemSnapshot.Take(adapters, tweak);
        var file = Path.Combine(Path.GetTempPath(), $"elynt-journal-{Guid.NewGuid():N}.json");
        try
        {
            var applied = new Engine(Catalogue.Default, adapters, new ChangeJournal(new JsonFileJournalStore(file)), "S-1-5-21-test").Apply(id);
            Assert.True(applied.Succeeded, applied.Message);

            var restarted = new Engine(Catalogue.Default, adapters, new ChangeJournal(new JsonFileJournalStore(file)), "S-1-5-21-test");
            var reverted = restarted.Revert(id);

            Assert.True(reverted.Succeeded, reverted.Message);
            Assert.Equal(before, Elynt.Tests.Shared.SystemSnapshot.Take(adapters, tweak));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Undo_refuses_to_switch_a_power_plan_someone_else_chose_since()
    {
        _power.Active = PowerPlans.Balanced;
        var engine = NewEngine();
        engine.Apply(GamingTweaks.HighPerformancePlan.Id);
        _power.Active = PowerPlans.PowerSaver;

        var reverted = engine.Revert(GamingTweaks.HighPerformancePlan.Id);

        Assert.False(reverted.Succeeded);
        Assert.Equal(PowerPlans.PowerSaver, _power.Active);
    }

    [Fact]
    public void Undo_refuses_when_something_else_set_useplatformclock_since()
    {
        _boot.Set("useplatformclock", "Yes");
        var engine = NewEngine();
        engine.Apply(GamingTweaks.UsePlatformClockRemoved.Id);
        _boot.Set("useplatformclock", "No");

        Assert.False(engine.Revert(GamingTweaks.UsePlatformClockRemoved.Id).Succeeded);
        Assert.Equal("No", _boot.Get("useplatformclock"));
    }

    [Fact]
    public void Undo_refuses_when_windowed_games_optimizations_were_switched_off_in_Settings_since()
    {
        _registry.SetValue(DirectXSettings, RegistryValue.String("AutoHDREnable=1;"));
        var engine = NewEngine();
        engine.Apply(GamingTweaks.WindowedGamesOptimizationsOn.Id);
        _registry.SetValue(DirectXSettings, RegistryValue.String("AutoHDREnable=1;SwapEffectUpgradeEnable=0;"));

        Assert.False(engine.Revert(GamingTweaks.WindowedGamesOptimizationsOn.Id).Succeeded);
        Assert.Equal(RegistryValue.String("AutoHDREnable=1;SwapEffectUpgradeEnable=0;"), _registry.GetValue(DirectXSettings));
    }
}
