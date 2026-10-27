using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;
using Elynt.Tests.Shared;

namespace Elynt.Core.Tests;

/// <summary>The Security section, driven through the Engine.</summary>
public class SecurityTweakTests
{
    private const string User = "S-1-5-21-test";
    private static readonly RegistryLocation HvciEnabled = new(RegistryRoot.LocalMachine,
        @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");

    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();
    private readonly FakeGameSources _pc = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    private readonly FakeScanExclusions _exclusions = new();
    private readonly FakeDefenderState _defender = new() { TamperProtectionOn = false };

    private SystemAdapters Adapters() =>
        new(_registry, _system, Games: _pc) { ScanExclusions = _exclusions, DefenderState = _defender };

    private static string ExclusionFor(string exePath) => SecurityTweaks.GameFolderExclusion.IdFor(exePath);

    private Engine NewEngine() => new(Catalogue.Default, Adapters(), _journal, User);

    [Fact]
    public void Memory_Integrity_off_turns_off_HVCI_and_is_a_security_risk_that_needs_a_reboot()
    {
        _registry.SetValue(HvciEnabled, RegistryValue.DWord(1));

        var result = NewEngine().Apply(SecurityTweaks.MemoryIntegrityOff.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(RegistryValue.DWord(0), _registry.GetValue(HvciEnabled));
        var view = result.Tweak!;
        Assert.Equal(Sections.Security, view.Section);
        Assert.Equal(RiskFlags.SecurityRisk | RiskFlags.NeedsReboot, view.Risks);
        Assert.True(view.PendingReboot);
        Assert.False(view.ProfileEligible);
    }

    [Fact]
    public void Memory_Integrity_off_names_the_installed_Vanguard_and_FACEIT_games_but_still_applies()
    {
        _pc.File(@"C:\Riot Games\VALORANT\live\VALORANT.exe");
        _pc.Key(@"SOFTWARE\WOW6432Node\Valve\Steam", ("InstallPath", @"C:\Steam"))
            .File(@"C:\Steam\steamapps\appmanifest_730.acf",
                "\"AppState\"\n{\n\t\"appid\"\t\t\"730\"\n\t\"name\"\t\t\"Counter-Strike 2\"\n\t\"installdir\"\t\t\"Counter-Strike Global Offensive\"\n}\n")
            .Directory(@"C:\Steam\steamapps\common\Counter-Strike Global Offensive");
        var engine = NewEngine();
        engine.AddGame(@"C:\Riot Games\VALORANT\live\VALORANT.exe");

        var result = engine.Apply(SecurityTweaks.MemoryIntegrityOff.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(
            [(AntiCheat.Vanguard, "VALORANT"), (AntiCheat.FaceIt, "Counter-Strike 2")],
            result.Tweak!.AffectedGames.Select(g => (g.AntiCheat, g.Game)).OrderBy(g => g.AntiCheat));
        Assert.Contains("VALORANT (Riot Vanguard)", result.Message);
        Assert.Contains("Counter-Strike 2 (FACEIT)", result.Message);
    }

    [Fact]
    public void Turning_Memory_Integrity_back_on_puts_back_the_exact_prior_value()
    {
        _registry.SetValue(HvciEnabled, RegistryValue.DWord(1));
        var engine = NewEngine();
        engine.Apply(SecurityTweaks.MemoryIntegrityOff.Id);

        var result = engine.UndoTweak(SecurityTweaks.MemoryIntegrityOff.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(RegistryValue.DWord(1), _registry.GetValue(HvciEnabled));
    }

    [Fact]
    public void Excluding_a_game_folder_adds_it_to_Defender_scan_exclusions_with_a_security_warning()
    {
        var result = NewEngine().Apply(ExclusionFor(@"D:\Games\Apex\r5apex.exe"));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal([@"D:\Games\Apex"], _exclusions.Paths);
        var view = result.Tweak!;
        Assert.Equal(Sections.Security, view.Section);
        Assert.Equal(RiskFlags.SecurityRisk, view.Risks);
        Assert.False(view.ProfileEligible);
        Assert.Contains("isn't scanned", view.WhatItDoes);
    }

    [Fact]
    public void A_game_folder_exclusion_is_journaled_and_undo_removes_only_it()
    {
        _exclusions.Paths.Add(@"E:\Work");
        var engine = NewEngine();
        var id = ExclusionFor(@"D:\Games\Apex\r5apex.exe");
        engine.Apply(id);

        var change = Assert.Single(engine.ListHistory());
        Assert.Equal(id, change.TweakId);
        var undone = engine.UndoChange(change.Id);

        Assert.True(undone.Succeeded, undone.Message);
        Assert.Equal([@"E:\Work"], _exclusions.Paths);
    }

    [Fact]
    public void A_game_folder_exclusion_can_be_undone_after_the_service_restarts()
    {
        var store = new InMemoryJournalStore();
        var id = ExclusionFor(@"D:\Games\Apex\r5apex.exe");
        new Engine(Catalogue.Default, Adapters(), new ChangeJournal(store), User).Apply(id);

        var restarted = new Engine(Catalogue.Default, Adapters(), new ChangeJournal(store), User);
        var undone = restarted.UndoTweak(id);

        Assert.True(undone.Succeeded, undone.Message);
        Assert.Empty(_exclusions.Paths);
    }

    [Fact]
    public void A_folder_the_user_had_already_excluded_shows_on_and_is_kept()
    {
        _exclusions.Paths.Add(@"D:\Games\Apex");
        var engine = NewEngine();
        var id = ExclusionFor(@"D:\Games\Apex\r5apex.exe");

        Assert.True(engine.Apply(id).Succeeded);
        Assert.False(engine.UndoTweak(id).Succeeded);

        Assert.Equal([@"D:\Games\Apex"], _exclusions.Paths);
    }

    [Theory]
    [InlineData(@"C:\game.exe")]
    [InlineData(@"C:\Windows\System32\game.exe")]
    [InlineData(@"C:\Program Files\game.exe")]
    [InlineData(@"C:\Users\aden\game.exe")]
    [InlineData(@"C:\Users\aden\Downloads\game.exe")]
    public void A_drive_system_or_user_folder_is_never_excluded(string exePath)
    {
        var result = NewEngine().Apply(ExclusionFor(exePath));

        Assert.False(result.Succeeded);
        Assert.Contains("never allowed", result.Message);
        Assert.Empty(_exclusions.Paths);
    }

    // The documented policy values the dControl-style Defender off writes, under the Group Policy key.
    private static readonly RegistryLocation DisableAntiSpyware = new(RegistryRoot.LocalMachine,
        @"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware");
    private static readonly RegistryLocation DisableRealtime = new(RegistryRoot.LocalMachine,
        @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring");

    [Fact]
    public void Defender_off_is_a_Security_tweak_that_needs_confirmation_a_reboot_and_is_never_in_a_profile()
    {
        var view = NewEngine().ListTweaks().Single(t => t.Id == SecurityTweaks.DefenderOff.Id);

        Assert.Equal(Sections.Security, view.Section);
        Assert.Equal(RiskFlags.SecurityRisk | RiskFlags.NeedsReboot, view.Risks);
        Assert.False(view.ProfileEligible);
        Assert.NotNull(view.ApplyWarning);
        Assert.Contains("learn.microsoft.com", view.Sources.Single().Url);
    }

    [Fact]
    public void Defender_off_refuses_while_Tamper_Protection_is_on_and_gives_the_step_by_step_guide()
    {
        _defender.TamperProtectionOn = true;

        var result = NewEngine().Apply(SecurityTweaks.DefenderOff.Id, new ApplyOptions(ConfirmWarning: true));

        Assert.False(result.Succeeded);
        Assert.Contains("Tamper Protection", result.Message);
        Assert.Contains("Windows Security", result.Message);
        Assert.Contains("Virus & threat protection", result.Message);
        // Nothing was written: Defender's policy is untouched while Tamper Protection is on.
        Assert.Null(_registry.GetValue(DisableAntiSpyware));
        Assert.Null(_registry.GetValue(DisableRealtime));
        // The view also carries the guide so the page can show it and grey the switch before the user tries.
        Assert.NotNull(result.Tweak!.BlockedReason);
        Assert.Contains("Tamper Protection", result.Tweak.BlockedReason);
    }

    [Fact]
    public void Defender_off_refuses_when_Defender_state_cannot_be_read_so_it_fails_safe()
    {
        var engine = new Engine(Catalogue.Default, new SystemAdapters(_registry, _system, Games: _pc)
        {
            ScanExclusions = _exclusions,
            // No DefenderState adapter: the app can't confirm Tamper Protection is off, so it must refuse.
        }, _journal, User);

        var result = engine.Apply(SecurityTweaks.DefenderOff.Id, new ApplyOptions(ConfirmWarning: true));

        Assert.False(result.Succeeded);
        Assert.Contains("Tamper Protection", result.Message);
        Assert.Null(_registry.GetValue(DisableAntiSpyware));
    }

    [Fact]
    public void Defender_off_needs_the_user_to_read_and_confirm_the_warning_first()
    {
        var result = NewEngine().Apply(SecurityTweaks.DefenderOff.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("confirm", result.Message);
        Assert.Null(_registry.GetValue(DisableAntiSpyware));
    }

    [Fact]
    public void Defender_off_writes_the_documented_policy_values_when_Tamper_Protection_is_off()
    {
        var result = NewEngine().Apply(SecurityTweaks.DefenderOff.Id, new ApplyOptions(ConfirmWarning: true));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(RegistryValue.DWord(1), _registry.GetValue(DisableAntiSpyware));
        Assert.Equal(RegistryValue.DWord(1), _registry.GetValue(DisableRealtime));
        Assert.Equal(TweakState.On, result.Tweak!.State);
        Assert.True(result.Tweak.PendingReboot);
        Assert.Null(result.Tweak.BlockedReason);
    }

    [Fact]
    public void Turning_Defender_back_on_is_one_click_and_restores_the_exact_journalled_state()
    {
        // The user had tightened one policy value by hand beforehand; undo must put that back, not delete it.
        _registry.SetValue(DisableRealtime, RegistryValue.DWord(0));
        var before = RegistrySnapshot.Take(_registry, SecurityTweaks.DefenderOff);
        var engine = NewEngine();
        Assert.True(engine.Apply(SecurityTweaks.DefenderOff.Id, new ApplyOptions(ConfirmWarning: true)).Succeeded);

        var result = engine.UndoTweak(SecurityTweaks.DefenderOff.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(RegistryValue.DWord(0), _registry.GetValue(DisableRealtime));
        Assert.Null(_registry.GetValue(DisableAntiSpyware));
        Assert.Equal(before, RegistrySnapshot.Take(_registry, SecurityTweaks.DefenderOff));
    }

    [Fact]
    public void Turning_Defender_back_on_works_even_after_the_service_restarts()
    {
        var store = new InMemoryJournalStore();
        new Engine(Catalogue.Default, Adapters(), new ChangeJournal(store), User)
            .Apply(SecurityTweaks.DefenderOff.Id, new ApplyOptions(ConfirmWarning: true));

        var restarted = new Engine(Catalogue.Default, Adapters(), new ChangeJournal(store), User);
        var result = restarted.UndoTweak(SecurityTweaks.DefenderOff.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Null(_registry.GetValue(DisableAntiSpyware));
        Assert.Null(_registry.GetValue(DisableRealtime));
    }

    [Fact]
    public void Defender_off_is_never_allowed_in_a_per_game_profile()
    {
        var profile = new Core.Profiles.GameProfile(@"C:\Games\x.exe") { TweakIds = [SecurityTweaks.DefenderOff.Id] };

        var result = NewEngine().SaveProfile(profile);

        Assert.False(result.Succeeded);
    }
}
