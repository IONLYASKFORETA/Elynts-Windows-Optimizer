using Elynt.Core;
using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;
using Elynt.Tests.Shared;

namespace Elynt.Windows.Tests;

/// <summary>The same catalogue round trip as the logic tests, against the real registry of the signed-in user.</summary>
public sealed class RealRegistryRoundTripTests : IDisposable
{
    private const string ScratchRoot = @"Software\ElyntOptimizerTests";
    private readonly string _scratchKey = $@"{ScratchRoot}\{Guid.NewGuid():N}\Nested";
    private static readonly string CurrentUserSid =
        OperatingSystem.IsWindows() ? System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value : "";

    // HKEY_USERS\<sid>, as the service sees the signed-in user, not this process's HKEY_CURRENT_USER.
    private readonly WindowsRegistry _registry = new(CurrentUserSid);

    public RealRegistryRoundTripTests() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs the real Windows registry.");

    public static TheoryData<string> CatalogueTweaks() => new(CatalogueRoundTrip.ApplicableIds(Catalogue.Default));

    private readonly WindowsBootConfig _boot = new();
    private readonly WindowsPowerPlans _power = new();

    /// <summary>
    /// Machine-wide changes (HKLM, boot settings, power plans) only run on CI's throwaway Windows machines, as admin,
    /// never on a developer's own PC.
    /// </summary>
    private static bool OnDisposableRunner =>
        OperatingSystem.IsWindows() &&
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase) &&
        new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

    [Theory]
    [MemberData(nameof(CatalogueTweaks))]
    public void Every_catalogue_tweak_reverts_to_the_exact_starting_state(string tweakId)
    {
        var tweak = Catalogue.Default.Get(tweakId);
        var machineWide = tweak.Steps.Any(step => step.Target is not RegistryTarget { Location.Root: RegistryRoot.CurrentUser });
        Assert.SkipWhen(machineWide && !OnDisposableRunner,
            "Changes machine-wide settings, so it only runs on CI's throwaway Windows machines as admin.");
        Assert.SkipWhen(tweak.Steps.OfType<Core.Power.PowerPlanStep>().Any(p => p.CreateFrom is null && !_power.Exists(p.Plan)),
            "This machine doesn't have that power plan.");

        var engine = NewEngine(Catalogue.Default);
        // Hidden tweaks (e.g. a version pin for another build) aren't listed; a per-game copy is listed only once applied.
        var view = engine.ListTweaks().SingleOrDefault(t => t.Id == tweakId);
        var perGameCopy = tweakId.Contains('|');
        Assert.SkipWhen(view is null && !perGameCopy, "Hidden on this machine because it doesn't apply here.");
        Assert.SkipWhen(view is { Applicable: false }, $"Doesn't apply to this machine: {view?.NotApplicableReason}");
        Assert.SkipWhen(view?.State == TweakState.On, "Already in the desired state on this machine, so there is nothing to undo.");

        CatalogueRoundTrip.Run(engine, Adapters(), tweak);
    }

    [Fact]
    public void A_forced_useplatformclock_is_removed_and_put_back()
    {
        Assert.SkipUnless(OnDisposableRunner, "Changes the boot configuration, so it only runs on CI's throwaway machines.");
        var before = _boot.Get("useplatformclock");
        try
        {
            _boot.Set("useplatformclock", "Yes");
            Assert.Equal("Yes", _boot.Get("useplatformclock"));

            CatalogueRoundTrip.Run(NewEngine(Catalogue.Default), Adapters(), GamingTweaks.UsePlatformClockRemoved);

            Assert.Equal("Yes", _boot.Get("useplatformclock"));
        }
        finally
        {
            if (before is null)
                _boot.Delete("useplatformclock");
            else
                _boot.Set("useplatformclock", before);
        }
    }

    private SystemAdapters Adapters() => new(_registry, new WindowsSystemInfo()) { Boot = _boot, Power = _power, PnpDevices = new WindowsDevices() };

    [Fact]
    public void Keys_and_values_the_tweak_created_are_removed_again()
    {
        var tweak = ScratchTweak();

        CatalogueRoundTrip.Run(NewEngine(new Catalogue([tweak])), Adapters(), tweak);

        Assert.False(_registry.KeyExists(RegistryRoot.CurrentUser, _scratchKey));
    }

    [Fact]
    public void A_value_of_a_different_type_comes_back_with_its_type_and_bytes()
    {
        var tweak = ScratchTweak();
        var location = ((RegistryValueStep)tweak.Steps[0]).Location;
        _registry.SetValue(location, RegistryValue.DWord(7));

        CatalogueRoundTrip.Run(NewEngine(new Catalogue([tweak])), Adapters(), tweak);

        Assert.Equal(RegistryValue.DWord(7), _registry.GetValue(location));
    }

    [Fact]
    public void Undo_leaves_a_value_alone_that_something_else_changed_since()
    {
        var tweak = ScratchTweak();
        var location = ((RegistryValueStep)tweak.Steps[0]).Location;
        var engine = NewEngine(new Catalogue([tweak]));
        Assert.True(engine.Apply(tweak.Id).Succeeded);
        _registry.SetValue(location, RegistryValue.String("changed elsewhere"));

        var result = engine.Revert(tweak.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(location.ToString(), result.Message);
        Assert.Equal(RegistryValue.String("changed elsewhere"), _registry.GetValue(location));
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
            return;
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(ScratchRoot, throwOnMissingSubKey: false);
    }

    private TweakDefinition ScratchTweak() => new(
        "test.scratch", Sections.Gaming, "Scratch", "Test only.", "None.", [], EvidenceTier.Proven, "Puts it back.",
        [new RegistryValueStep(new(RegistryRoot.CurrentUser, _scratchKey, "Value"), RegistryValue.String("0"))]);

    private Engine NewEngine(Catalogue catalogue) => new(
        catalogue,
        Adapters(),
        new ChangeJournal(new InMemoryJournalStore()),
        CurrentUserSid);
}
