using Elynt.Core.Apps;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>Debloat &amp; Privacy app removal (ticket 9), driven through the Engine with fake app adapters.</summary>
public class AppRemovalTests
{
    private const string User = "S-1-5-21-test";

    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();
    private readonly FakeRemovableApps _apps = new();
    private readonly FakeOptionalFeatures _features = new();
    private readonly InMemoryJournalStore _store = new();

    private Engine NewEngine() =>
        new(Catalogue.Default, new SystemAdapters(_registry, _system) { Apps = _apps, Features = _features },
            new ChangeJournal(_store), User);

    [Fact]
    public void Removing_copilot_is_labelled_needs_download_and_undo_reinstalls_it()
    {
        _apps.Install("Microsoft.Copilot");
        var engine = NewEngine();

        var view = engine.ListTweaks().Single(t => t.Id == "debloat.remove-copilot");
        Assert.Equal(Reversibility.NeedsDownload, view.Reversibility);
        Assert.Equal(TweakState.Off, view.State);

        var removed = engine.Apply("debloat.remove-copilot");
        Assert.True(removed.Succeeded, removed.Message);
        Assert.False(_apps.IsInstalled("Microsoft.Copilot"));
        Assert.Equal(TweakState.On, removed.Tweak!.State);

        var undone = engine.Revert("debloat.remove-copilot");
        Assert.True(undone.Succeeded, undone.Message);
        Assert.True(_apps.IsInstalled("Microsoft.Copilot"));
        Assert.Equal(["Microsoft.Copilot"], _apps.Reinstalls);
    }

    [Fact]
    public void Curated_preinstalled_apps_can_each_be_removed_for_this_account_and_reinstalled()
    {
        var curated = new[]
        {
            "Clipchamp.Clipchamp", "Microsoft.BingNews", "Microsoft.BingWeather",
            "Microsoft.MicrosoftSolitaireCollection", "MSTeams",
        };
        foreach (var package in curated)
            _apps.Install(package);
        var engine = NewEngine();

        foreach (var package in curated)
        {
            var view = engine.ListTweaks().Single(t =>
                t.Id.StartsWith("debloat.remove-app.") && t.ExactChange.Any(c => c.Contains($"({package})")));
            Assert.Equal(Reversibility.NeedsDownload, view.Reversibility);

            Assert.True(engine.Apply(view.Id).Succeeded);
            Assert.False(_apps.IsInstalled(package));
            Assert.True(engine.Revert(view.Id).Succeeded);
            Assert.True(_apps.IsInstalled(package));
        }
    }

    [Fact]
    public void Xbox_apps_game_bar_edge_and_webview2_are_never_offered_for_removal()
    {
        var offered = Catalogue.Default.Tweaks
            .SelectMany(t => t.Steps)
            .Select(s => s.Target)
            .OfType<PackageTarget>()
            .Select(p => p.PackageName)
            .ToList();

        Assert.NotEmpty(offered);
        Assert.DoesNotContain(offered, p => p.Contains("Xbox") || p.Contains("GamingApp") || p.Contains("Edge"));
    }

    [Fact]
    public void A_reinstall_that_fails_says_so_and_the_removal_can_be_undone_later()
    {
        _apps.Install("Microsoft.Copilot");
        var engine = NewEngine();
        Assert.True(engine.Apply("debloat.remove-copilot").Succeeded);
        _apps.ReinstallFails.Add("Microsoft.Copilot");

        var failed = engine.Revert("debloat.remove-copilot");

        Assert.False(failed.Succeeded);
        Assert.Contains("Couldn't reach the Microsoft Store", failed.Message);
        Assert.True(failed.Tweak!.CanUndo);

        _apps.ReinstallFails.Clear();
        Assert.True(engine.Revert("debloat.remove-copilot").Succeeded);
        Assert.True(_apps.IsInstalled("Microsoft.Copilot"));
    }

    [Fact]
    public void Removal_for_future_accounts_is_destructive_and_needs_its_own_confirmation()
    {
        _apps.Provision("Microsoft.Copilot");
        _apps.Provision("Clipchamp.Clipchamp");
        _apps.Install("Clipchamp.Clipchamp");
        var engine = NewEngine();

        var view = engine.ListTweaks().Single(t => t.Id == "debloat.deprovision-preinstalled");
        Assert.Equal(Reversibility.Destructive, view.Reversibility);
        Assert.Contains("new accounts", view.DestructiveWarning);

        var refused = engine.Apply("debloat.deprovision-preinstalled", new ApplyOptions(ConfirmWarning: true));
        Assert.False(refused.Succeeded);
        Assert.True(_apps.IsProvisioned("Microsoft.Copilot"));

        var applied = engine.Apply("debloat.deprovision-preinstalled", new ApplyOptions(ConfirmDestructive: true));
        Assert.True(applied.Succeeded, applied.Message);
        Assert.False(_apps.IsProvisioned("Microsoft.Copilot"));
        Assert.False(_apps.IsProvisioned("Clipchamp.Clipchamp"));
        // Only future accounts: this account's apps are a separate, per-app choice.
        Assert.True(_apps.IsInstalled("Clipchamp.Clipchamp"));
        Assert.Equal(TweakState.On, applied.Tweak!.State);
    }

    [Fact]
    public void Undoing_removal_for_future_accounts_is_refused_up_front_with_its_warning()
    {
        _apps.Provision("Microsoft.Copilot");
        var engine = NewEngine();
        var applied = engine.Apply("debloat.deprovision-preinstalled", new ApplyOptions(ConfirmDestructive: true));
        Assert.True(applied.Succeeded, applied.Message);
        var change = engine.ListHistory().Single();

        foreach (var undo in new[] { engine.Revert("debloat.deprovision-preinstalled"), engine.UndoChange(change.Id) })
        {
            Assert.False(undo.Succeeded);
            Assert.StartsWith("This can't be undone.", undo.Message);
            Assert.Contains(applied.Tweak!.DestructiveWarning!, undo.Message);
        }
        Assert.False(change.CanUndo);
        Assert.False(_apps.IsProvisioned("Microsoft.Copilot"));
    }

    [Fact]
    public void A_removal_can_still_be_undone_after_the_service_restarts()
    {
        _apps.Install("Microsoft.BingNews");
        _features.Add("Recall", enabled: true);
        _apps.Provision("Microsoft.BingNews");
        var first = NewEngine();
        Assert.True(first.Apply("debloat.remove-app.microsoft.bingnews").Succeeded);
        Assert.True(first.Apply("privacy.remove-recall").Succeeded);
        Assert.True(first.Apply("debloat.deprovision-preinstalled", new ApplyOptions(ConfirmDestructive: true)).Succeeded);

        var restarted = NewEngine();

        Assert.True(restarted.Revert("debloat.remove-app.microsoft.bingnews").Succeeded);
        Assert.True(_apps.IsInstalled("Microsoft.BingNews"));
        Assert.True(restarted.Revert("privacy.remove-recall").Succeeded);
        Assert.True(_features.IsEnabled("Recall"));
        Assert.StartsWith("This can't be undone.", restarted.Revert("debloat.deprovision-preinstalled").Message);
    }

    [Fact]
    public void Recall_removal_is_hidden_on_pcs_without_recall_and_refused_there()
    {
        var engine = NewEngine();

        Assert.DoesNotContain(engine.ListTweaks(), t => t.Id == "privacy.remove-recall");
        var refused = engine.Apply("privacy.remove-recall");
        Assert.False(refused.Succeeded);
        Assert.Contains("Copilot+", refused.Message);
    }

    [Fact]
    public void Recall_removal_on_a_copilot_plus_pc_turns_the_feature_off_after_a_reboot_and_undo_turns_it_back_on()
    {
        _features.Add("Recall", enabled: true);
        _system.BootTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var engine = NewEngine();

        var view = engine.ListTweaks().Single(t => t.Id == "privacy.remove-recall");
        Assert.True(view.Applicable);
        Assert.True(view.Risks.HasFlag(RiskFlags.NeedsReboot));
        Assert.Equal(Reversibility.Reversible, view.Reversibility);

        var applied = engine.Apply("privacy.remove-recall");
        Assert.True(applied.Succeeded, applied.Message);
        Assert.False(_features.IsEnabled("Recall"));
        Assert.True(applied.Tweak!.PendingReboot);

        Assert.True(engine.Revert("privacy.remove-recall").Succeeded);
        Assert.True(_features.IsEnabled("Recall"));
    }

    [Theory]
    [InlineData("debloat.remove-onedrive", "Microsoft.OneDrive", "cloud")]
    [InlineData("debloat.remove-store", "Microsoft.WindowsStore", "Game Pass")]
    public void Onedrive_and_store_removal_warn_first_and_change_nothing_until_the_warning_is_confirmed(
        string tweakId, string package, string warningMentions)
    {
        _apps.Install(package);
        var engine = NewEngine();

        var view = engine.ListTweaks().Single(t => t.Id == tweakId);
        Assert.Contains(warningMentions, view.ApplyWarning);

        var refused = engine.Apply(tweakId);
        Assert.False(refused.Succeeded);
        Assert.Contains(warningMentions, refused.Message);
        Assert.True(_apps.IsInstalled(package));
        Assert.Empty(engine.ListHistory());

        var removed = engine.Apply(tweakId, new ApplyOptions(ConfirmWarning: true));
        Assert.True(removed.Succeeded, removed.Message);
        Assert.False(_apps.IsInstalled(package));
    }

    [Fact]
    public void Undoing_onedrive_removal_reinstalls_it_from_winget_community_source()
    {
        var winget = new FakePackageManager();
        _apps.Install("Microsoft.OneDrive");
        var engine = new Engine(Catalogue.Default, new SystemAdapters(_registry, _system, Packages: winget) { Apps = _apps },
            new ChangeJournal(_store), User);
        Assert.True(engine.Apply("debloat.remove-onedrive", new ApplyOptions(ConfirmWarning: true)).Succeeded);

        var undone = engine.Revert("debloat.remove-onedrive");

        Assert.True(undone.Succeeded, undone.Message);
        Assert.Equal([("Microsoft.OneDrive", "winget")], winget.Installs);
    }

    [Fact]
    public void Undoing_store_removal_puts_the_store_back_with_wsreset()
    {
        _apps.Install("Microsoft.WindowsStore");
        var engine = NewEngine();
        Assert.True(engine.Apply("debloat.remove-store", new ApplyOptions(ConfirmWarning: true)).Succeeded);

        var view = engine.ListTweaks().Single(t => t.Id == "debloat.remove-store");
        Assert.Equal(Reversibility.NeedsDownload, view.Reversibility);
        Assert.Contains("wsreset -i", view.Undo);

        Assert.True(engine.Revert("debloat.remove-store").Succeeded);
        Assert.Equal(["Microsoft.WindowsStore"], _apps.Reinstalls);
    }
}
