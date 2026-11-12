using Elynt.Core.Journal;
using Elynt.Core.Power;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>
/// Pick-one choices, driven through the Engine: tweaks that fight each other (the power plans, the monthly update
/// delays), of which at most one is on. Choosing one undoes the app's change for the one that was on.
/// </summary>
public class PickOneChoiceTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();
    private readonly FakePowerPlans _power = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    private Engine NewEngine() => new(Catalogue.Default, new SystemAdapters(_registry, _system) { Power = _power }, _journal, "S-1-5-21-test");

    private static readonly string PowerPlan = GamingTweaks.PowerPlanChoice.Id;
    private static readonly string High = GamingTweaks.HighPerformancePlan.Id;
    private static readonly string Ultimate = GamingTweaks.UltimatePerformancePlan.Id;

    [Fact]
    public void Choosing_Ultimate_after_High_performance_switches_to_Ultimate_and_undoes_the_apps_High_performance_change()
    {
        _power.Active = PowerPlans.Balanced;
        var engine = NewEngine();
        Assert.True(engine.Choose(PowerPlan, High).Succeeded);

        var result = engine.Choose(PowerPlan, Ultimate);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(PowerPlans.ElyntUltimatePerformance, _power.Active);
        Assert.Equal(
            [(High, TweakState.Off), (Ultimate, TweakState.On)],
            result.Tweaks.Select(t => (t.Id, t.State)));
        Assert.Equal(ChangeState.Undone, engine.ListHistory().Single(c => c.TweakId == High).State);
    }

    [Fact]
    public void Choosing_the_default_undoes_the_apps_change_and_puts_back_the_plan_from_before()
    {
        _power.Active = PowerPlans.PowerSaver;
        var plansBefore = _power.Plans.ToList();
        var engine = NewEngine();
        Assert.True(engine.Choose(PowerPlan, High).Succeeded);
        Assert.True(engine.Choose(PowerPlan, Ultimate).Succeeded);

        var result = engine.Choose(PowerPlan, null);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(PowerPlans.PowerSaver, _power.Active);
        Assert.Equal(plansBefore, _power.Plans);
        Assert.All(result.Tweaks, t => Assert.Equal(TweakState.Off, t.State));
        Assert.Contains("Windows default", result.Message);
    }

    [Fact]
    public void Choosing_the_default_when_no_option_is_on_changes_nothing()
    {
        _power.Active = PowerPlans.Balanced;

        var result = NewEngine().Choose(PowerPlan, null);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(PowerPlans.Balanced, _power.Active);
        Assert.Empty(_journal.EntriesFor("S-1-5-21-test"));
    }

    [Fact]
    public void An_option_Windows_set_shows_as_already_set()
    {
        _power.Active = PowerPlans.HighPerformance;

        var tweaks = NewEngine().ListTweaks().Where(t => t.Choice?.Id == PowerPlan).ToList();

        Assert.Equal([(High, true), (Ultimate, false)], tweaks.Select(t => (t.Id, t.AlreadySet)));
        Assert.Equal(TweakState.On, tweaks[0].State);
    }

    [Fact]
    public void Choosing_over_an_option_Windows_set_applies_the_new_one_and_the_default_goes_back_to_what_Windows_set()
    {
        _power.Active = PowerPlans.HighPerformance;
        var engine = NewEngine();

        var chosen = engine.Choose(PowerPlan, Ultimate);
        Assert.True(chosen.Succeeded, chosen.Message);
        Assert.Equal(PowerPlans.ElyntUltimatePerformance, _power.Active);

        var back = engine.Choose(PowerPlan, null);
        Assert.True(back.Succeeded, back.Message);
        Assert.Equal(PowerPlans.HighPerformance, _power.Active);
        Assert.True(back.Tweaks.Single(t => t.Id == High).AlreadySet);
    }

    [Fact]
    public void The_default_cant_undo_an_option_the_app_didnt_set()
    {
        _power.Active = PowerPlans.HighPerformance;

        var result = NewEngine().Choose(PowerPlan, null);

        Assert.False(result.Succeeded);
        Assert.Contains("Nothing was changed", result.Message);
        Assert.Equal(PowerPlans.HighPerformance, _power.Active);
    }

    [Fact]
    public void Each_choice_is_its_own_change_in_the_Undo_history_and_undoing_it_there_works_as_before()
    {
        _power.Active = PowerPlans.Balanced;
        var engine = NewEngine();
        engine.Choose(PowerPlan, High);
        engine.Choose(PowerPlan, Ultimate);

        var history = engine.ListHistory();
        Assert.Equal([(Ultimate, ChangeState.Applied, true), (High, ChangeState.Undone, false)],
            history.Select(c => (c.TweakId, c.State, c.CanUndo)));

        var undone = engine.UndoChange(history[0].Id);

        Assert.True(undone.Succeeded, undone.Message);
        Assert.Equal(PowerPlans.Balanced, _power.Active);
        Assert.DoesNotContain(PowerPlans.ElyntUltimatePerformance, _power.Plans);
    }

    [Fact]
    public void A_choice_that_fails_puts_back_the_option_that_was_on_and_says_so()
    {
        _power.Active = PowerPlans.Balanced;
        var engine = NewEngine();
        engine.Choose(PowerPlan, Ultimate);
        _power.Remove(PowerPlans.HighPerformance);

        var result = engine.Choose(PowerPlan, High);

        Assert.False(result.Succeeded);
        Assert.Equal(PowerPlans.ElyntUltimatePerformance, _power.Active);
        Assert.Contains("Ultimate is back on", result.Message);
        Assert.Equal(TweakState.On, result.Tweaks.Single(t => t.Id == Ultimate).State);
    }

    [Fact]
    public void A_refused_choice_leaves_the_option_that_was_on()
    {
        _system.Edition = WindowsEdition.Home;
        _registry.SetValue(DelayDays, Registry.RegistryValue.DWord(7));

        var result = NewEngine().Choose(UpdateDelay, WindowsUpdateTweaks.DeferQuality30.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(Registry.RegistryValue.DWord(7), _registry.GetValue(DelayDays));
    }

    private static readonly string UpdateDelay = WindowsUpdateTweaks.UpdateDelayChoice.Id;

    private static readonly Registry.RegistryLocation DelayDays =
        new(Registry.RegistryRoot.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferQualityUpdatesPeriodInDays");

    private static readonly Registry.RegistryLocation DelayOn =
        new(Registry.RegistryRoot.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferQualityUpdates");

    [Fact]
    public void Switching_the_update_delay_from_7_to_30_days_leaves_30_and_the_default_removes_the_policy()
    {
        var engine = NewEngine();
        Assert.True(engine.Choose(UpdateDelay, WindowsUpdateTweaks.DeferQuality7.Id).Succeeded);

        var thirty = engine.Choose(UpdateDelay, WindowsUpdateTweaks.DeferQuality30.Id);

        Assert.True(thirty.Succeeded, thirty.Message);
        Assert.Equal(Registry.RegistryValue.DWord(30), _registry.GetValue(DelayDays));
        Assert.Equal(Registry.RegistryValue.DWord(1), _registry.GetValue(DelayOn));
        Assert.Equal([TweakState.Off, TweakState.On], thirty.Tweaks.Select(t => t.State));

        var none = engine.Choose(UpdateDelay, null);

        Assert.True(none.Succeeded, none.Message);
        Assert.Null(_registry.GetValue(DelayDays));
        Assert.Null(_registry.GetValue(DelayOn));
    }

    [Fact]
    public void The_power_plan_and_the_update_delay_are_each_one_choice()
    {
        var tweaks = NewEngine().ListTweaks();

        Assert.Equal([High, Ultimate], tweaks.Where(t => t.Choice?.Id == PowerPlan).Select(t => t.Id));
        Assert.Equal([WindowsUpdateTweaks.DeferQuality7.Id, WindowsUpdateTweaks.DeferQuality30.Id],
            tweaks.Where(t => t.Choice?.Id == UpdateDelay).Select(t => t.Id));
    }

    public static TheoryData<string, TweakDefinition> MisfitOptions() => new()
    {
        { "on another page", LabelTests.TestTweak("test.b") with { Section = Sections.Security } },
        { "set per game", LabelTests.TestTweak("test.b") with { PerGame = true } },
        { "destructive", LabelTests.TestTweak("test.b") with { Reversibility = Reversibility.Destructive, DestructiveWarning = "Gone." } },
    };

    /// <summary>Choosing another option undoes the one before, so every option must be undoable on its own, on one page.</summary>
    [Theory]
    [MemberData(nameof(MisfitOptions))]
    public void An_option_that_cant_share_a_row_is_a_catalogue_bug(string why, TweakDefinition misfit)
    {
        var choice = new ChoiceGroup("test.choice", $"An option {why}", "Picks one.", "Windows default",
            [new("test.a", "A"), new(misfit.Id, "B")]);

        Assert.Throws<ArgumentException>(() => new Catalogue([LabelTests.TestTweak("test.a"), misfit], choices: [choice]));
    }

    [Fact]
    public void A_tweak_in_two_choices_is_a_catalogue_bug()
    {
        ChoiceGroup Choice(string id) => new(id, "Test", "Picks one.", "Windows default", [new("test.a", "A"), new("test.b", "B")]);

        Assert.Throws<ArgumentException>(() =>
            new Catalogue([LabelTests.TestTweak("test.a"), LabelTests.TestTweak("test.b")], choices: [Choice("test.one"), Choice("test.two")]));
    }

    [Fact]
    public void An_unknown_choice_or_an_option_of_another_choice_is_refused()
    {
        var engine = NewEngine();

        Assert.False(engine.Choose("gaming.no-such-choice", null).Succeeded);
        var wrong = engine.Choose(PowerPlan, WindowsUpdateTweaks.DeferQuality7.Id);
        Assert.False(wrong.Succeeded);
        Assert.Null(_registry.GetValue(DelayDays));
    }
}
