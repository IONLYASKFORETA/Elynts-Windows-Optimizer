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

    [Theory]
    [MemberData(nameof(CatalogueTweaks))]
    public void Every_catalogue_tweak_reverts_to_the_exact_starting_state(string tweakId) =>
        CatalogueRoundTrip.Run(NewEngine(Catalogue.Default), Adapters(), Catalogue.Default.Get(tweakId));

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
        new SystemAdapters(_registry, new WindowsSystemInfo()),
        new ChangeJournal(new InMemoryJournalStore()),
        CurrentUserSid);
}
