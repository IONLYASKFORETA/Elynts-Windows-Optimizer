using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>The Gaming section's proven and situational tweaks, driven through the Engine.</summary>
public class GamingTweakTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    private Engine NewEngine() => new(Catalogue.Default, new SystemAdapters(_registry, _system), _journal, "S-1-5-21-test");

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
}
