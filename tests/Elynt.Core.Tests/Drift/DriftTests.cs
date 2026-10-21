using Elynt.Core.Drift;
using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>Drift after Windows updates (ticket 17), driven through the Engine with fake adapters.</summary>
public class DriftTests
{
    private const string User = "S-1-5-21-test";
    private const string StartRecommendations = "debloat.start-recommendations-off";

    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();
    private readonly FakeRemovableApps _apps = new();
    private readonly FakeOptionalFeatures _features = new();
    private readonly InMemoryJournalStore _journal = new();
    private readonly InMemoryDriftStore _drift = new();

    private Engine NewEngine() =>
        new(Catalogue.Default, new SystemAdapters(_registry, _system) { Apps = _apps, Features = _features },
            new ChangeJournal(_journal), User, driftStore: _drift);

    /// <summary>What a Windows update does: writes its own value over each of the tweak's registry values.</summary>
    private void WindowsResets(string tweakId, uint value = 1, int steps = int.MaxValue)
    {
        foreach (var step in Catalogue.Default.Get(tweakId).Steps.OfType<RegistryValueStep>().Take(steps))
            _registry.SetValue(step.Location, RegistryValue.DWord(value));
    }

    private IEnumerable<RegistryValue?> Values(string tweakId) =>
        Catalogue.Default.Get(tweakId).Steps.OfType<RegistryValueStep>().Select(s => _registry.GetValue(s.Location));

    [Fact]
    public void A_change_windows_undid_is_reported_as_drift()
    {
        var engine = NewEngine();
        Assert.True(engine.Apply(StartRecommendations).Succeeded);
        Assert.Empty(engine.CheckDrift().Drifted);

        WindowsResets(StartRecommendations);
        var report = NewEngine().CheckDrift();

        var item = Assert.Single(report.Drifted);
        Assert.Equal(StartRecommendations, item.TweakId);
        Assert.Equal(DriftKind.SettingChanged, item.Kind);
        Assert.Equal(TweakState.Off, NewEngine().ListTweaks().Single(t => t.Id == StartRecommendations).State);
    }

    [Fact]
    public void Reapplying_drift_turns_the_tweak_back_on_and_undo_then_returns_to_what_windows_set()
    {
        Assert.True(NewEngine().Apply(StartRecommendations).Succeeded);
        WindowsResets(StartRecommendations, value: 7);
        var engine = NewEngine();
        Assert.Single(engine.CheckDrift().Drifted);

        var result = engine.ReapplyDrift(null);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(StartRecommendations, Assert.Single(result.Changes).TweakId);
        Assert.Equal(TweakState.On, engine.ListTweaks().Single(t => t.Id == StartRecommendations).State);
        Assert.Empty(engine.CheckDrift().Drifted);

        // The before-state is now Windows' new value, not the value from before the app's first change.
        Assert.True(engine.Revert(StartRecommendations).Succeeded);
        Assert.All(Values(StartRecommendations), v => Assert.Equal(RegistryValue.DWord(7), v));
    }

    [Fact]
    public void Reapplying_partial_drift_keeps_the_original_before_state_of_the_parts_windows_left_alone()
    {
        // Nothing was set before the app's change, so undo deletes the values it wrote.
        Assert.True(NewEngine().Apply(StartRecommendations).Succeeded);
        WindowsResets(StartRecommendations, value: 7, steps: 1);
        var engine = NewEngine();
        var item = Assert.Single(engine.CheckDrift().Drifted);
        Assert.Single(item.Changes);

        Assert.True(engine.ReapplyDrift([StartRecommendations]).Succeeded);
        Assert.True(engine.Revert(StartRecommendations).Succeeded);

        Assert.Equal([RegistryValue.DWord(7), null], Values(StartRecommendations));
    }

    [Fact]
    public void After_a_reapply_the_undo_history_offers_only_the_new_change()
    {
        Assert.True(NewEngine().Apply(StartRecommendations).Succeeded);
        WindowsResets(StartRecommendations);
        var engine = NewEngine();
        engine.CheckDrift();
        Assert.True(engine.ReapplyDrift(null).Succeeded);

        var history = engine.ListHistory().Where(c => c.TweakId == StartRecommendations).ToList();
        Assert.Equal(2, history.Count);
        Assert.True(history[0].CanUndo);
        Assert.Equal(ChangeState.Applied, history[0].State);
        Assert.False(history[1].CanUndo);
    }

    [Fact]
    public void A_removed_app_that_came_back_is_drift_and_reapplying_removes_it_again()
    {
        _apps.Install("Microsoft.Copilot");
        Assert.True(NewEngine().Apply("debloat.remove-copilot").Succeeded);

        _apps.Install("Microsoft.Copilot");
        var engine = NewEngine();
        var item = Assert.Single(engine.CheckDrift().Drifted);
        Assert.Equal(DriftKind.AppReturned, item.Kind);
        Assert.Equal("debloat.remove-copilot", item.TweakId);

        Assert.True(engine.ReapplyDrift(["debloat.remove-copilot"]).Succeeded);
        Assert.False(_apps.IsInstalled("Microsoft.Copilot"));

        // Undo still reinstalls it, since it was installed when the app removed it again.
        Assert.True(engine.Revert("debloat.remove-copilot").Succeeded);
        Assert.True(_apps.IsInstalled("Microsoft.Copilot"));
    }

    [Fact]
    public void The_first_check_and_a_check_after_a_windows_build_change_are_full_and_others_are_quick()
    {
        Assert.Equal(DriftCheckKind.Full, NewEngine().CheckDrift().Kind);
        Assert.Equal(DriftCheckKind.Quick, NewEngine().CheckDrift().Kind);

        _system.OsBuild = "26200.1";
        Assert.Equal(DriftCheckKind.Full, NewEngine().CheckDrift().Kind);
        Assert.Equal(DriftCheckKind.Quick, NewEngine().CheckDrift().Kind);
    }

    [Fact]
    public void A_quick_check_only_looks_at_whether_changes_are_in_place_and_a_full_check_also_rechecks_they_fit_the_pc()
    {
        var tweak = LabelTests.TestTweak("test.nvidia-only") with { Applicability = new(RequiresAll: HardwareTraits.NvidiaGpu) };
        Engine Fixed() => new(new Catalogue([tweak]), new SystemAdapters(_registry, _system), new ChangeJournal(_journal), User, driftStore: _drift);
        Assert.True(Fixed().Apply(tweak.Id).Succeeded);
        Assert.Equal(DriftCheckKind.Full, Fixed().CheckDrift().Kind);

        // The GPU was swapped; the setting itself is untouched.
        _system.Hardware = HardwareTraits.AmdCpu;
        var quick = Fixed().CheckDrift();
        Assert.Equal(DriftCheckKind.Quick, quick.Kind);
        Assert.Empty(quick.Drifted);
        Assert.Empty(quick.NewlyUnsupported);

        _system.OsBuild = "26200.1";
        var full = Fixed().CheckDrift();
        Assert.Equal(DriftCheckKind.Full, full.Kind);
        Assert.Equal(tweak.Id, Assert.Single(full.NewlyUnsupported).TweakId);
    }

    [Fact]
    public void A_quick_check_still_marks_a_drifted_change_whose_target_is_gone_rather_than_offering_it()
    {
        _features.Add("Recall", enabled: true);
        Assert.True(NewEngine().Apply("privacy.remove-recall").Succeeded);
        NewEngine().CheckDrift();

        var features = new FakeOptionalFeatures();
        var engine = new Engine(Catalogue.Default, new SystemAdapters(_registry, _system) { Apps = _apps, Features = features },
            new ChangeJournal(_journal), User, driftStore: _drift);

        var quick = engine.CheckDrift();
        Assert.Equal(DriftCheckKind.Quick, quick.Kind);
        Assert.Empty(quick.Drifted);
        Assert.Equal("privacy.remove-recall", Assert.Single(quick.NewlyUnsupported).TweakId);
    }

    [Fact]
    public void A_change_whose_target_a_new_build_removed_is_marked_unsupported_once_and_then_skipped_quietly()
    {
        _features.Add("Recall", enabled: true);
        Assert.True(NewEngine().Apply("privacy.remove-recall").Succeeded);

        // The update drops the feature altogether.
        _system.OsBuild = "26200.1";
        var features = new FakeOptionalFeatures();
        var engine = new Engine(Catalogue.Default, new SystemAdapters(_registry, _system) { Apps = _apps, Features = features },
            new ChangeJournal(_journal), User, driftStore: _drift);

        var first = engine.CheckDrift();
        Assert.Empty(first.Drifted);
        Assert.Equal("privacy.remove-recall", Assert.Single(first.NewlyUnsupported).TweakId);

        var second = engine.CheckDrift();
        Assert.Empty(second.Drifted);
        Assert.Empty(second.NewlyUnsupported);
        Assert.Empty(engine.ReapplyDrift(null).Changes);
        Assert.Contains("not supported on this Windows build",
            engine.ListTweaks().Single(t => t.Id == "privacy.remove-recall").NotApplicableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_reapply_that_fails_marks_the_change_unsupported_on_this_build_until_the_build_changes()
    {
        Assert.True(NewEngine().Apply(StartRecommendations).Succeeded);
        WindowsResets(StartRecommendations);
        var engine = NewEngine();
        engine.CheckDrift();

        _registry.FailOnceAfter(0);
        var failed = engine.ReapplyDrift(null);
        Assert.False(failed.Succeeded);
        Assert.False(Assert.Single(failed.Changes).Succeeded);

        // No repeated errors: later checks and re-applies on this build skip it.
        var again = NewEngine().CheckDrift();
        Assert.Empty(again.Drifted);
        Assert.Empty(again.NewlyUnsupported);
        Assert.Empty(NewEngine().ReapplyDrift(null).Changes);

        _system.OsBuild = "26200.1";
        Assert.Equal(StartRecommendations, Assert.Single(NewEngine().CheckDrift().Drifted).TweakId);
    }

    [Fact]
    public void Automatic_reapply_is_off_by_default_so_a_check_only_reports()
    {
        Assert.False(NewEngine().GetDriftSettings().AutoReapply);
        Assert.True(NewEngine().Apply(StartRecommendations).Succeeded);
        WindowsResets(StartRecommendations);

        var report = NewEngine().CheckDrift();

        Assert.Single(report.Drifted);
        Assert.Empty(report.Reapplied);
        Assert.Equal(TweakState.Off, NewEngine().ListTweaks().Single(t => t.Id == StartRecommendations).State);
    }

    [Fact]
    public void Automatic_reapply_puts_back_instant_changes_but_still_asks_for_reboot_needing_ones()
    {
        const string DiagTrack = "privacy.diagtrack-service-off";
        Assert.True(NewEngine().SetAutoReapply(true).AutoReapply);
        _apps.Install("Microsoft.Copilot");
        foreach (var id in new[] { StartRecommendations, DiagTrack, "debloat.remove-copilot" })
            Assert.True(NewEngine().Apply(id).Succeeded);
        WindowsResets(StartRecommendations);
        WindowsResets(DiagTrack, value: 2);
        _apps.Install("Microsoft.Copilot");

        var report = NewEngine().CheckDrift();

        Assert.Equal(["debloat.remove-copilot", StartRecommendations], report.Reapplied.Select(d => d.TweakId).Order(StringComparer.Ordinal));
        var asked = Assert.Single(report.Drifted);
        Assert.Equal(DiagTrack, asked.TweakId);
        Assert.True(asked.NeedsReboot);
        Assert.False(_apps.IsInstalled("Microsoft.Copilot"));
    }

    [Fact]
    public void Reapplying_a_destructive_change_needs_a_separate_confirmation()
    {
        const string Deprovision = "debloat.deprovision-preinstalled";
        _apps.Provision("Microsoft.Copilot");
        Assert.True(NewEngine().Apply(Deprovision, new ApplyOptions(ConfirmDestructive: true)).Succeeded);
        _apps.Provision("Microsoft.Copilot");
        var engine = NewEngine();
        Assert.True(engine.SetAutoReapply(true).AutoReapply);

        var report = engine.CheckDrift();
        var item = Assert.Single(report.Drifted);
        Assert.True(item.NeedsConfirmation);
        Assert.Empty(report.Reapplied);

        Assert.False(engine.ReapplyDrift([Deprovision]).Succeeded);
        Assert.True(_apps.IsProvisioned("Microsoft.Copilot"));

        Assert.True(engine.ReapplyDrift([Deprovision], confirmDestructive: true).Succeeded);
        Assert.False(_apps.IsProvisioned("Microsoft.Copilot"));
    }

    [Fact]
    public void The_report_says_in_plain_words_what_windows_undid()
    {
        _apps.Install("Microsoft.Copilot");
        Assert.True(NewEngine().Apply(StartRecommendations).Succeeded);
        Assert.True(NewEngine().Apply("debloat.remove-copilot").Succeeded);
        WindowsResets(StartRecommendations);
        _apps.Install("Microsoft.Copilot");

        var report = NewEngine().CheckDrift();

        Assert.Equal(2, report.Drifted.Count);
        Assert.Contains("Windows undid 2 changes", report.Message);
    }

    [Fact]
    public void Drift_state_survives_a_service_restart_in_the_json_file_store()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drift-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonFileDriftStore(path);
            store.Save(User, DriftState.Initial with
            {
                LastBuild = "26100.1",
                AutoReapply = true,
                Unsupported = new Dictionary<string, UnsupportedMark> { ["x"] = new("26100.1", "1.0", "gone") },
            });

            var loaded = new JsonFileDriftStore(path).Load(User);

            Assert.Equal("26100.1", loaded.LastBuild);
            Assert.True(loaded.AutoReapply);
            Assert.Equal("gone", loaded.Unsupported["x"].Reason);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
