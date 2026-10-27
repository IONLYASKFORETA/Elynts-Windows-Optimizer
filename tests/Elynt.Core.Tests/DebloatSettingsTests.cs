using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>The Debloat &amp; Privacy settings (ticket 8), driven through the Engine.</summary>
public class DebloatSettingsTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();

    private Engine NewEngine() =>
        new(Catalogue.Default, new SystemAdapters(_registry, _system), new ChangeJournal(new InMemoryJournalStore()), "S-1-5-21-test");

    private IReadOnlyList<TweakView> Section() =>
        NewEngine().ListTweaks().Where(t => DebloatTweaks.All.Any(d => d.Id == t.Id)).ToList();

    [Fact]
    public void Debloat_and_privacy_lists_every_setting_the_spec_asks_for_besides_app_removal()
    {
        Assert.Equal(
            [
                "privacy.diagnostic-data-minimum",
                "privacy.diagtrack-service-off",
                "privacy.feedback-prompts-off",
                "privacy.tailored-experiences-off",
                "debloat.start-recommendations-off",
                "debloat.lock-screen-tips-off",
                "debloat.settings-suggestions-off",
                "debloat.explorer-promos-off",
                "debloat.bing-in-start-search-off",
                "debloat.widgets-off",
                "privacy.recall-off",
                "debloat.edge-background-off",
                "debloat.edge-nags-off",
                "debloat.game-dvr-background-capture-off",
            ],
            Section().Select(t => t.Id));
    }

    [Fact]
    public void Every_setting_is_reversible_and_removes_no_app_so_edge_xbox_and_game_bar_stay_installed()
    {
        Assert.All(Section(), view =>
        {
            Assert.Equal(Reversibility.Reversible, view.Reversibility);
            Assert.False(view.ProfileEligible);
            Assert.All(view.ExactChange, change => Assert.StartsWith(@"Set HK", change));
        });
    }

    [Fact]
    public void Game_dvr_turns_off_only_background_recording_and_leaves_game_bar_working()
    {
        var engine = NewEngine();

        var result = engine.Apply("debloat.game-dvr-background-capture-off");

        Assert.True(result.Succeeded, result.Message);
        // Only "Record what happened". AppCaptureEnabled and GameDVR_Enabled turn off all capture, on-demand too.
        Assert.Equal(
            [@"Set HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled to 0 (0x00000000) (REG_DWORD)"],
            result.Tweak!.ExactChange);
    }

    [Fact]
    public void Turning_off_diagtrack_is_pending_a_reboot_and_undo_puts_the_start_type_back()
    {
        var start = new Registry.RegistryLocation(Registry.RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start");
        _registry.SetValue(start, Registry.RegistryValue.DWord(2));
        _system.BootTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var engine = NewEngine();

        var applied = engine.Apply("privacy.diagtrack-service-off");

        Assert.True(applied.Succeeded, applied.Message);
        Assert.True(applied.Tweak!.PendingReboot);
        Assert.Equal(Registry.RegistryValue.DWord(4), _registry.GetValue(start));

        Assert.True(engine.UndoTweak("privacy.diagtrack-service-off").Succeeded);
        Assert.Equal(Registry.RegistryValue.DWord(2), _registry.GetValue(start));
    }

    [Fact]
    public void Recall_is_not_applicable_before_windows_11_24h2()
    {
        _system.OsBuild = "22631.4317";
        var engine = NewEngine();

        var view = engine.ListTweaks().Single(t => t.Id == "privacy.recall-off");

        Assert.False(view.Applicable);
        Assert.False(engine.Apply("privacy.recall-off").Succeeded);
    }
}
