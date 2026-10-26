using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;
using Elynt.Tests.Shared;

namespace Elynt.Core.Tests;

/// <summary>
/// A destructive change has nothing to put back, so it is never offered for undo (ADR 0002): the Undo history says it
/// can't be undone, undo refuses it before touching anything, and Restore everything leaves it out and says so.
/// </summary>
public class DestructiveChangeTests
{
    private readonly FakeRegistry _registry = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    /// <summary>
    /// Writes a registry value like any tweak but is labelled destructive, so an undo that went ahead anyway would show
    /// in the registry.
    /// </summary>
    private static readonly TweakDefinition Destructive =
        LabelTests.TestTweak("test.destructive", new RegistryValueStep(ValueOf("test.destructive"), RegistryValue.DWord(1))) with
        {
            Name = "Delete the old files",
            Reversibility = Reversibility.Destructive,
            DestructiveWarning = "The old files are deleted for good.",
        };

    private static readonly TweakDefinition Plain =
        LabelTests.TestTweak("test.plain", new RegistryValueStep(ValueOf("test.plain"), RegistryValue.DWord(1))) with
        {
            Name = "Turn on the plain setting",
        };

    [Fact]
    public void A_destructive_change_is_in_the_undo_history_as_one_that_cant_be_undone()
    {
        var engine = EngineFor(Destructive);
        Applied(engine, Destructive);

        var change = Assert.Single(engine.ListHistory());

        Assert.Equal((ChangeState.Applied, false, true), (change.State, change.CanUndo, change.Destructive));
    }

    [Fact]
    public void Undoing_a_destructive_change_is_refused_up_front_with_its_warning_and_touches_nothing()
    {
        var engine = EngineFor(Destructive);
        Applied(engine, Destructive);
        var change = engine.ListHistory().Single();
        var applied = RegistrySnapshot.Take(_registry, Destructive);

        foreach (var result in new[] { engine.UndoChange(change.Id), engine.Revert(Destructive.Id) })
        {
            Assert.False(result.Succeeded);
            Assert.Equal("This can't be undone. The old files are deleted for good. Nothing was changed.", result.Message);
        }

        Assert.Equal(applied, RegistrySnapshot.Take(_registry, Destructive));
        Assert.Equal(ChangeState.Applied, engine.ListHistory().Single().State);
    }

    [Fact]
    public void A_destructive_tweak_the_app_applied_is_on_with_nothing_to_undo_and_is_not_already_set()
    {
        var engine = EngineFor(Destructive);
        Applied(engine, Destructive);

        var view = engine.ListTweaks().Single();

        Assert.Equal((TweakState.On, false, false), (view.State, view.CanUndo, view.AlreadySet));
    }

    [Fact]
    public void Restore_everything_puts_back_the_other_changes_and_names_the_one_it_left()
    {
        _registry.SetValue(ValueOf(Plain.Id), RegistryValue.DWord(0)); // the user's own choice, for undo to bring back
        var plainBefore = RegistrySnapshot.Take(_registry, Plain);
        var engine = EngineFor(Plain, Destructive);
        Applied(engine, Plain);
        Applied(engine, Destructive);
        var destructiveApplied = RegistrySnapshot.Take(_registry, Destructive);

        var result = engine.RestoreEverything();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(
            "Put back the 1 change that can be undone. 1 change was left as it is, because it can never be undone: Delete the old files.",
            result.Message);
        Assert.Equal([Plain.Name], result.Changes.Select(c => c.TweakName));
        Assert.Equal(plainBefore, RegistrySnapshot.Take(_registry, Plain));
        Assert.Equal(destructiveApplied, RegistrySnapshot.Take(_registry, Destructive));
    }

    [Fact]
    public void When_the_only_change_left_cant_be_undone_restore_everything_isnt_offered_and_says_so_if_asked()
    {
        var engine = EngineFor(Plain, Destructive);
        Applied(engine, Destructive);
        Applied(engine, Plain);
        Assert.True(engine.Revert(Plain.Id).Succeeded);

        // The window offers Restore everything only while some change in the history can be undone.
        Assert.DoesNotContain(engine.ListHistory(), change => change.CanUndo);
        var result = engine.RestoreEverything();

        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(result.Changes);
        Assert.Equal("Nothing to undo. 1 change was left as it is, because it can never be undone: Delete the old files.", result.Message);
    }

    // ---- helpers ----

    private static RegistryLocation ValueOf(string id) => new(RegistryRoot.CurrentUser, @"Software\ElyntTest", id);

    /// <summary>Applies the tweak as the window does, with the separate confirmation a destructive one needs.</summary>
    private static void Applied(Engine engine, TweakDefinition tweak)
    {
        var result = engine.Apply(tweak.Id, new ApplyOptions(ConfirmDestructive: true));
        Assert.True(result.Succeeded, result.Message);
    }

    private Engine EngineFor(params TweakDefinition[] tweaks) =>
        new(new Catalogue(tweaks), new SystemAdapters(_registry, new FakeSystemInfo()), _journal, "S-1-5-21-test");
}
