using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;

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

    private SystemAdapters Adapters() => new(_registry, _system, Games: _pc) { ScanExclusions = _exclusions };

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

        var result = engine.Revert(SecurityTweaks.MemoryIntegrityOff.Id);

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
        var undone = restarted.Revert(id);

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
        Assert.False(engine.Revert(id).Succeeded);

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
}
