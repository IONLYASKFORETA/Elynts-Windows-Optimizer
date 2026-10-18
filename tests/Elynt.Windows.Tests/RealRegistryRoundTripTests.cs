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

    public static TheoryData<string> CatalogueTweaks() => new(Catalogue.Default.Tweaks.Select(t => t.Id));

    [Theory]
    [MemberData(nameof(CatalogueTweaks))]
    public void Every_catalogue_tweak_reverts_to_the_exact_starting_state(string tweakId)
    {
        var tweak = Catalogue.Default.Get(tweakId);
        // This is the registry round trip: app removals and Windows features are covered by the logic tests, so
        // CI never uninstalls apps from the runner.
        if (tweak.Steps.Any(step => step is not RegistryValueStep))
            Assert.Skip("Changes more than registry values; covered by the fake-adapter logic tests.");
        // CI runs on Windows Server, so tweaks for other builds or editions are skipped there, not failed.
        if (tweak.Applicability.WhyNot(new WindowsSystemInfo()) is { } whyNot)
            Assert.Skip($"Not applicable on this machine: {whyNot}");

        var engine = NewEngine(Catalogue.Default);
        // A runner image may already ship a policy set (e.g. telemetry). Then there is nothing the app could undo.
        if (engine.ListTweaks().Single(t => t.Id == tweakId).State == TweakState.On)
            Assert.Skip("Already set on this machine, so there is nothing to apply and undo.");

        CatalogueRoundTrip.Run(engine, _registry, tweak);
    }

    [Fact]
    public void Keys_and_values_the_tweak_created_are_removed_again()
    {
        var tweak = ScratchTweak();

        CatalogueRoundTrip.Run(NewEngine(new Catalogue([tweak])), _registry, tweak);

        Assert.False(_registry.KeyExists(RegistryRoot.CurrentUser, _scratchKey));
    }

    [Fact]
    public void A_value_of_a_different_type_comes_back_with_its_type_and_bytes()
    {
        var tweak = ScratchTweak();
        var location = ((RegistryValueStep)tweak.Steps[0]).Location;
        _registry.SetValue(location, RegistryValue.DWord(7));

        CatalogueRoundTrip.Run(NewEngine(new Catalogue([tweak])), _registry, tweak);

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
        new SystemAdapters(_registry, new WindowsSystemInfo()),
        new ChangeJournal(new InMemoryJournalStore()),
        CurrentUserSid);
}
