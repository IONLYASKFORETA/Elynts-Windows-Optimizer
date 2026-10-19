using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>Labels, warnings, destructive confirmation, pending reboot and applicability, driven through the Engine.</summary>
public class LabelTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();
    private readonly ManualClock _clock = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    [Fact]
    public void Every_label_and_risk_flag_comes_from_the_catalogue()
    {
        var tweak = TestTweak("test.risky") with
        {
            Reversibility = Reversibility.NeedsDownload,
            Risks = RiskFlags.SecurityRisk | RiskFlags.NeedsReboot | RiskFlags.CanStopBoot,
            AntiCheat = [new AntiCheatConflict(AntiCheat.Vanguard, AntiCheatSeverity.WontLaunch, "VAN: RESTRICTION: 5")],
        };

        var view = Single(EngineFor(tweak));

        Assert.Equal(Reversibility.NeedsDownload, view.Reversibility);
        Assert.Equal(RiskFlags.SecurityRisk | RiskFlags.NeedsReboot | RiskFlags.CanStopBoot, view.Risks);
        Assert.Equal([new AntiCheatConflict(AntiCheat.Vanguard, AntiCheatSeverity.WontLaunch, "VAN: RESTRICTION: 5")], view.AntiCheat);
    }

    [Fact]
    public void A_plain_tweak_is_reversible_with_no_risk_flags()
    {
        var view = Single(EngineFor(TestTweak("test.plain")));

        Assert.Equal(Reversibility.Reversible, view.Reversibility);
        Assert.Equal(RiskFlags.None, view.Risks);
        Assert.Empty(view.AntiCheat);
    }

    [Fact]
    public void A_destructive_tweak_is_refused_without_the_confirmation_and_nothing_changes()
    {
        var tweak = Destructive(TestTweak("test.destructive"));
        var engine = EngineFor(tweak);

        var result = engine.Apply(tweak.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("can't be undone", result.Message);
        Assert.Null(_registry.GetValue(TestValue));
        Assert.Equal(TweakState.Off, result.Tweak!.State);
        Assert.False(result.Tweak.CanUndo);
    }

    [Fact]
    public void A_destructive_tweak_applies_once_confirmed()
    {
        var tweak = Destructive(TestTweak("test.destructive"));
        var engine = EngineFor(tweak);

        var result = engine.Apply(tweak.Id, new ApplyOptions(ConfirmDestructive: true));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(RegistryValue.DWord(1), _registry.GetValue(TestValue));
        Assert.Equal("Deletes the files for good.", result.Tweak!.DestructiveWarning);
    }

    [Fact]
    public void The_confirmation_is_not_needed_for_a_reversible_tweak()
    {
        var tweak = TestTweak("test.plain");

        Assert.True(EngineFor(tweak).Apply(tweak.Id).Succeeded);
    }

    public static TheoryData<string, ChangeTarget> NeverTouchChanges() => new()
    {
        { "test signing", new BootSettingTarget("testsigning", "Yes") },
        { "driver signature enforcement off", new BootSettingTarget("nointegritychecks", "Yes") },
        { "integrity checks off through load options", new BootSettingTarget("loadoptions", "DDISABLE_INTEGRITY_CHECKS") },
        { "kernel debugging", new BootSettingTarget("debug", "Yes") },
        { "boot debugging", new BootSettingTarget("bootdebug", "On") },
        { "Driver Verifier", Reg(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "VerifyDrivers") },
        { "Driver Verifier level", Reg(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "VerifyDriverLevel") },
        { "WinRing0", new DriverTarget("WinRing0x64.sys") },
        { "InpOut", new DriverTarget(@"C:\Tools\inpoutx64.sys") },
        { "old MSI RTCore", new DriverTarget("RTCore64.sys") },
        { "Vanguard service", new ServiceTarget("vgc", ServiceStart.Manual) },
        { "Vanguard driver", new ServiceTarget("VGK", ServiceStart.Disabled) },
        { "BattlEye service", new ServiceTarget("BEService", ServiceStart.Disabled) },
        { "Easy Anti-Cheat service", new ServiceTarget("EasyAntiCheat_EOS", ServiceStart.Disabled) },
        { "FACEIT driver", new ServiceTarget("FACEIT", ServiceStart.Disabled) },
        { "Windows Update service", new ServiceTarget("wuauserv", ServiceStart.Disabled) },
        { "Windows Update Medic", new ServiceTarget("WaaSMedicSvc", ServiceStart.Disabled) },
        { "boot-critical storage driver", new ServiceTarget("stornvme", ServiceStart.Disabled) },
        { "protected service via the registry", Reg(@"SYSTEM\CurrentControlSet\Services\vgc", "Start") },
        { "protected service via a numbered control set", Reg(@"SYSTEM\ControlSet001\Services\wuauserv", "Start") },
        { "every service via a registry pattern", new RegistryPatternTarget(
            new(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Services\*", "Start"), RegistryValue.DWord(4)) },
        { "every service's settings via a registry pattern", new RegistryPatternTarget(
            new(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Services\*\Parameters", "Anything"), RegistryValue.DWord(1)) },
        { "quality updates deferred past 30 days", Reg(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferQualityUpdatesPeriodInDays", 31) },
        { "Documents", new FileTarget(@"C:\Users\aden\Documents\notes.txt") },
        { "Downloads", new FileTarget(@"C:\Users\aden\Downloads") },
        { "Desktop", new FileTarget(@"C:\Users\aden\Desktop\game.lnk") },
        { "Pictures", new FileTarget(@"c:\users\aden\pictures\x.png") },
        { "game saves", new FileTarget(@"C:\Users\aden\Saved Games\Game\save.dat") },
        { "OneDrive", new FileTarget(@"C:\Users\aden\OneDrive - Home\file.txt") },
        { "Edge", new PackageTarget("Microsoft.MicrosoftEdge.Stable") },
        { "WebView2", new PackageTarget("Microsoft.EdgeWebView2Runtime") },
    };

    [Theory]
    [MemberData(nameof(NeverTouchChanges))]
    public void A_never_touch_change_is_refused_even_in_Advanced(string what, ChangeTarget target)
    {
        var tweak = TestTweak("test.forbidden", new RegistryValueStep(TestValue, RegistryValue.DWord(1)), new DeclaredStep(target));
        var engine = EngineFor(tweak);

        var result = engine.Apply(tweak.Id, new ApplyOptions(ConfirmDestructive: true, Advanced: true));

        Assert.False(result.Succeeded, what);
        Assert.Contains("is never allowed", result.Message);
        Assert.Null(_registry.GetValue(TestValue));
        Assert.False(result.Tweak!.CanUndo);
    }

    public static TheoryData<string, ChangeTarget> AllowedChanges() => new()
    {
        { "removing a forced platform clock", new BootSettingTarget("useplatformclock", null) },
        { "an ordinary service", new ServiceTarget("Spooler", ServiceStart.Manual) },
        { "temp files", new FileTarget(@"C:\Users\aden\AppData\Local\Temp\x.tmp") },
        { "a 30 day quality deferral", Reg(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferQualityUpdatesPeriodInDays", 30) },
        { "an ordinary app", new PackageTarget("Clipchamp.Clipchamp") },
        { "a signed vendor driver", new DriverTarget("PawnIO.sys") },
    };

    [Theory]
    [MemberData(nameof(AllowedChanges))]
    public void Ordinary_changes_pass_the_hard_rules(string what, ChangeTarget target)
    {
        Assert.True(HardRules.Violation(target) is null, what);
    }

    [Fact]
    public void No_catalogue_tweak_breaks_a_hard_rule()
    {
        foreach (var tweak in Catalogue.Default.Tweaks)
            foreach (var step in tweak.Steps)
                Assert.Null(HardRules.Violation(step.Target));
    }

    [Fact]
    public void A_reboot_needing_tweak_shows_pending_reboot_until_the_PC_restarts()
    {
        var tweak = TestTweak("test.reboot") with { Risks = RiskFlags.NeedsReboot };
        var engine = EngineFor(tweak);
        Assert.False(Single(engine).PendingReboot);

        var applied = engine.Apply(tweak.Id);
        Assert.True(applied.Tweak!.PendingReboot);
        Assert.True(Single(engine).PendingReboot);

        _clock.Now += TimeSpan.FromMinutes(5);
        _system.BootTime = _clock.Now;
        _clock.Now += TimeSpan.FromMinutes(1);

        Assert.False(Single(EngineFor(tweak)).PendingReboot);
        Assert.Equal(TweakState.On, Single(EngineFor(tweak)).State);
    }

    [Fact]
    public void Undoing_a_reboot_needing_tweak_is_pending_a_reboot_too()
    {
        var tweak = TestTweak("test.reboot") with { Risks = RiskFlags.NeedsReboot };
        var engine = EngineFor(tweak);
        engine.Apply(tweak.Id);
        _system.BootTime = _clock.Now += TimeSpan.FromMinutes(5);
        _clock.Now += TimeSpan.FromMinutes(1);
        Assert.False(Single(engine).PendingReboot);

        var reverted = engine.Revert(tweak.Id);

        Assert.True(reverted.Succeeded);
        Assert.True(reverted.Tweak!.PendingReboot);
    }

    [Fact]
    public void A_tweak_that_needs_no_reboot_is_never_pending_one()
    {
        var tweak = TestTweak("test.plain");
        var engine = EngineFor(tweak);

        Assert.False(engine.Apply(tweak.Id).Tweak!.PendingReboot);
    }

    [Fact]
    public void A_tweak_for_a_newer_build_is_marked_not_applicable_and_refused()
    {
        _system.OsBuild = "22631.4000";
        var tweak = TestTweak("test.new-build") with { Applicability = new Applicability(MinBuild: 26100) };
        var engine = EngineFor(tweak);

        var view = Single(engine);
        Assert.False(view.Applicable);
        Assert.Equal("Needs Windows build 26100 or newer; this PC has 22631.", view.NotApplicableReason);

        var result = engine.Apply(tweak.Id);
        Assert.False(result.Succeeded);
        Assert.Contains("doesn't apply to this PC", result.Message);
        Assert.Null(_registry.GetValue(TestValue));
    }

    [Fact]
    public void A_tweak_for_another_edition_is_marked_not_applicable()
    {
        _system.Edition = WindowsEdition.Home;
        var tweak = TestTweak("test.pro-only") with { Applicability = new Applicability(Editions: [WindowsEdition.Pro, WindowsEdition.Enterprise]) };

        var view = Single(EngineFor(tweak));

        Assert.False(view.Applicable);
        Assert.Equal("Needs Windows 11 Pro or Enterprise; this PC has Home.", view.NotApplicableReason);
    }

    [Fact]
    public void A_tweak_for_missing_hardware_can_be_hidden_and_is_still_refused()
    {
        _system.Hardware = HardwareTraits.IntelGpu | HardwareTraits.IntelCpu;
        var hidden = TestTweak("test.amd-only") with { Applicability = new Applicability(RequiresAll: HardwareTraits.AmdCpu, Hide: true) };
        var shown = TestTweak("test.any-gpu") with { Applicability = new Applicability(RequiresAny: HardwareTraits.NvidiaGpu | HardwareTraits.AmdGpu) };
        var engine = EngineFor(hidden, shown);

        var view = Assert.Single(engine.ListTweaks());
        Assert.Equal(shown.Id, view.Id);
        Assert.Equal("Needs one of: an NVIDIA GPU, an AMD GPU.", view.NotApplicableReason);
        Assert.False(engine.Apply(hidden.Id).Succeeded);
        Assert.Null(_registry.GetValue(TestValue));
    }

    [Fact]
    public void A_tweak_that_fits_this_PC_is_applicable()
    {
        var tweak = TestTweak("test.fits") with
        {
            Applicability = new Applicability(MinBuild: 22000, Editions: [WindowsEdition.Pro], RequiresAll: HardwareTraits.NvidiaGpu),
        };

        var view = Single(EngineFor(tweak));

        Assert.True(view.Applicable);
        Assert.Null(view.NotApplicableReason);
    }

    [Fact]
    public void A_change_made_before_the_PC_stopped_fitting_can_still_be_undone()
    {
        var tweak = TestTweak("test.nvidia") with { Applicability = new Applicability(RequiresAll: HardwareTraits.NvidiaGpu, Hide: true) };
        EngineFor(tweak).Apply(tweak.Id);

        _system.Hardware = HardwareTraits.AmdGpu;
        var engine = EngineFor(tweak);

        var view = Assert.Single(engine.ListTweaks());
        Assert.True(view.CanUndo);
        Assert.True(engine.Revert(tweak.Id).Succeeded);
        Assert.Null(_registry.GetValue(TestValue));
    }

    public static TheoryData<string, TweakDefinition> InconsistentTweaks() => new()
    {
        { "spell out what can't be undone", TestTweak("bad.destructive") with { Reversibility = Reversibility.Destructive } },
        { "needs a reboot", TestTweak("bad.reboot") with { ProfileEligible = true, Risks = RiskFlags.NeedsReboot } },
        { "destructive", TestTweak("bad.profile-destructive") with { ProfileEligible = true, Reversibility = Reversibility.Destructive, DestructiveWarning = "Gone." } },
        { "Security", TestTweak("bad.security") with { ProfileEligible = true, Section = Sections.Security } },
        { "signed-in session", TestTweak("bad.session") with { ProfileEligible = true, Refresh = SessionRefresh.MouseSettings } },
    };

    [Theory]
    [MemberData(nameof(InconsistentTweaks))]
    public void The_catalogue_refuses_inconsistent_labels(string problem, TweakDefinition tweak)
    {
        var error = Assert.Throws<ArgumentException>(() => new Catalogue([tweak]));

        Assert.Contains(problem, error.Message);
        Assert.Contains(tweak.Id, error.Message);
    }

    [Fact]
    public void An_instant_tweak_may_be_in_a_per_game_profile()
    {
        var tweak = TestTweak("test.instant") with { ProfileEligible = true };

        Assert.True(Single(EngineFor(tweak)).ProfileEligible);
    }

    // ---- helpers ----

    private static RegistryTarget Reg(string key, string name, uint value = 1) =>
        new(new RegistryLocation(RegistryRoot.LocalMachine, key, name), RegistryValue.DWord(value));

    /// <summary>A step that only declares what it would change. The hard rules must stop it before it runs.</summary>
    private sealed record DeclaredStep(ChangeTarget Declared) : TweakStep
    {
        public override ChangeTarget Target => Declared;
        public override string DescribeChange() => Declared.ToString();
        public override bool IsApplied(SystemAdapters system) => false;
        public override StepRecord Capture(SystemAdapters system) => throw new InvalidOperationException("Ran a step the hard rules should have stopped.");
        public override void Apply(SystemAdapters system) => throw new InvalidOperationException("Ran a step the hard rules should have stopped.");
    }

    private static TweakDefinition Destructive(TweakDefinition tweak) =>
        tweak with { Reversibility = Reversibility.Destructive, DestructiveWarning = "Deletes the files for good." };

    private static readonly RegistryLocation TestValue = new(RegistryRoot.CurrentUser, @"Software\ElyntTest", "Value");

    internal static TweakDefinition TestTweak(string id, params TweakStep[] steps) => new(
        Id: id,
        Section: Sections.Gaming,
        Name: id,
        WhatItDoes: "A test tweak.",
        Evidence: "None.",
        Sources: [],
        Tier: EvidenceTier.Situational,
        Undo: "Puts it back.",
        Steps: steps.Length > 0 ? steps : [new RegistryValueStep(TestValue, RegistryValue.DWord(1))]);

    private Engine EngineFor(params TweakDefinition[] tweaks) =>
        new(new Catalogue(tweaks), new SystemAdapters(_registry, _system), _journal, "S-1-5-21-test", clock: _clock);

    private static TweakView Single(Engine engine) => Assert.Single(engine.ListTweaks());

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
