using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;
using Elynt.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elynt.Core.Tests.Journal;

/// <summary>
/// Interrupted changes, at the Undo history's own interface: a change that fails part-way and can't be put back stays
/// in the Undo history with what was there before, so it can be undone, whether it was a new change or a re-apply
/// after drift.
/// </summary>
public class InterruptedChangeTests
{
    private const string User = "S-1-5-21-test";
    private readonly FakeRegistry _registry = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    // Three mouse values, so a change can fail after its first write.
    private static readonly TweakDefinition Tweak = GamingTweaks.EnhancePointerPrecisionOff;

    private UndoHistory NewHistory() => new(
        _journal, User, Catalogue.Default, new SystemAdapters(_registry, new FakeSystemInfo()), safetyNet: null,
        new FakeClock(DateTimeOffset.Parse("2026-10-27T15:00:00Z")), NullLogger.Instance);

    [Fact]
    public void A_change_that_fails_and_cant_be_put_back_stays_interrupted_and_can_be_undone()
    {
        SetOtherValues();
        var before = RegistrySnapshot.Take(_registry, Tweak);
        var history = NewHistory();

        // The second write fails, and so does every write after it, so putting the first one back fails too.
        _registry.DieAfter(1);
        var made = history.Make(Tweak, ChangeWording.Tweak, temporary: false);

        Assert.False(made.Succeeded);
        Assert.Contains("The prior state is saved", made.Message);
        var change = Assert.Single(history.List());
        Assert.Equal(ChangeState.Interrupted, change.State);
        Assert.True(change.CanUndo);

        _registry.Revive();
        Assert.True(history.UndoChange(change.Id).Succeeded);
        Assert.Equal(before, RegistrySnapshot.Take(_registry, Tweak));
    }

    [Fact]
    public void A_reapply_that_fails_and_cant_be_put_back_stays_interrupted_and_undoing_it_puts_back_every_part()
    {
        SetOtherValues();
        var history = NewHistory();
        Assert.True(history.Make(Tweak, ChangeWording.Tweak, temporary: false).Succeeded);
        var original = history.Newest(Tweak.Id)!;

        // Something else undoes the first two parts and leaves the third in place.
        _registry.SetValue(Locations[0], RegistryValue.DWord(9));
        _registry.SetValue(Locations[1], RegistryValue.DWord(9));

        // Re-applying makes the first part again, the second fails, and putting the first back fails too.
        _registry.DieAfter(1);
        var reapplied = history.Reapply(Tweak, original, drifted: [true, true, false]);

        Assert.False(reapplied.Succeeded);
        Assert.Contains("The prior state is saved", reapplied.Message);
        Assert.Null(reapplied.Unsupported);
        var change = history.List()[0];
        Assert.Equal(ChangeState.Interrupted, change.State);
        Assert.True(change.CanUndo);

        _registry.Revive();
        Assert.True(history.UndoChange(change.Id).Succeeded);

        // The drifted parts go back to what was set after the first change; the part still in place goes back to
        // before it. Both changes are closed, so nothing is left to undo.
        RegistryValue?[] expected = [RegistryValue.DWord(9), RegistryValue.DWord(9), RegistryValue.DWord(7)];
        Assert.Equal(expected, Locations.Select(_registry.GetValue));
        Assert.Equal(2, history.List().Count);
        Assert.All(history.List(), c => Assert.Equal(ChangeState.Undone, c.State));
        Assert.Null(history.Newest(Tweak.Id));
    }

    private static IReadOnlyList<RegistryLocation> Locations =>
        [.. Tweak.Steps.OfType<RegistryValueStep>().Select(step => step.Location)];

    private void SetOtherValues()
    {
        foreach (var location in Locations)
            _registry.SetValue(location, RegistryValue.DWord(7));
    }
}
