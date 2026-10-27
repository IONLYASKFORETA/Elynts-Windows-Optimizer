using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>
/// Already set: a tweak that is on because Windows, the user or another tool set it, with no change of the app's own
/// behind it. The Engine says so on the tweak's view, so the window labels it and locks its switch (ADR 0002).
/// </summary>
public class AlreadySetTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    private static readonly TweakDefinition Plain =
        LabelTests.TestTweak("test.plain", new RegistryValueStep(ValueOf("test.plain"), RegistryValue.DWord(1)));

    [Fact]
    public void A_tweak_whose_setting_is_already_in_place_with_nothing_in_the_journal_is_on_and_already_set()
    {
        SetStraightInTheRegistry(Plain);

        var view = Listed(EngineFor(Plain), Plain);

        Assert.Equal((TweakState.On, true), (view.State, view.AlreadySet));
    }

    [Fact]
    public void A_tweak_the_app_switched_on_is_not_already_set_and_nor_is_it_once_undone()
    {
        var engine = EngineFor(Plain);

        var applied = engine.Apply(Plain.Id).Tweak!;
        var undone = engine.UndoTweak(Plain.Id).Tweak!;

        Assert.Equal((TweakState.On, false), (applied.State, applied.AlreadySet));
        Assert.Equal((TweakState.Off, false), (undone.State, undone.AlreadySet));
    }

    [Fact]
    public void A_tweak_that_does_not_fit_this_PC_is_still_already_set_when_its_setting_is_in_place()
    {
        _system.OsBuild = "22631.4000";
        var newerBuild = Plain with { Applicability = new Applicability(MinBuild: 26100) };
        SetStraightInTheRegistry(newerBuild);

        var view = Listed(EngineFor(newerBuild), newerBuild);

        Assert.Equal((false, true), (view.Applicable, view.AlreadySet));
    }

    [Fact]
    public void A_blocked_tweak_is_still_already_set_when_its_setting_is_in_place()
    {
        // Defender's policy values written by another tool, while Tamper Protection is on and blocks the app's own.
        var defender = new FakeDefenderState { TamperProtectionOn = true };
        SetStraightInTheRegistry(SecurityTweaks.DefenderOff);
        var engine = new Engine(new Catalogue([SecurityTweaks.DefenderOff]),
            new SystemAdapters(_registry, _system) { DefenderState = defender }, _journal, "S-1-5-21-test");

        var view = Listed(engine, SecurityTweaks.DefenderOff);

        Assert.Equal((true, true), (view.Blocked, view.AlreadySet));
    }

    // ---- helpers ----

    private static RegistryLocation ValueOf(string id) => new(RegistryRoot.CurrentUser, @"Software\ElyntTest", id);

    /// <summary>What Windows, the user or another tool does: writes the tweak's own values, with no word to the app.</summary>
    private void SetStraightInTheRegistry(TweakDefinition tweak)
    {
        foreach (var step in tweak.Steps.OfType<RegistryValueStep>())
            _registry.SetValue(step.Location, step.Desired);
    }

    private static TweakView Listed(Engine engine, TweakDefinition tweak) => engine.ListTweaks().Single(t => t.Id == tweak.Id);

    private Engine EngineFor(params TweakDefinition[] tweaks) =>
        new(new Catalogue(tweaks), new SystemAdapters(_registry, _system), _journal, "S-1-5-21-test");
}
