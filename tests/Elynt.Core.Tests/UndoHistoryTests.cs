using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;
using Elynt.Tests.Shared;

namespace Elynt.Core.Tests;

/// <summary>The Undo history page: every journaled change, undoing any one of them, and restore everything.</summary>
public class UndoHistoryTests
{
    private const string User = "S-1-5-21-test";
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeRegistry _registry = new();
    private readonly FakeClock _clock = new(Start);
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    private static readonly TweakDefinition Mouse = GamingTweaks.EnhancePointerPrecisionOff;

    private static readonly TweakDefinition GameMode = new(
        "test.game-mode", Sections.Gaming, "Turn on Game Mode", "Test only.", "None.", [], EvidenceTier.Proven,
        "Puts it back.",
        [new RegistryValueStep(new(RegistryRoot.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled"), RegistryValue.DWord(1))]);

    private readonly Catalogue _catalogue = new([Mouse, GameMode]);

    [Fact]
    public void History_shows_time_tweak_and_state_for_every_change_newest_first()
    {
        var engine = NewEngine();
        engine.Apply(Mouse.Id);
        _clock.Advance(TimeSpan.FromMinutes(5));
        engine.Apply(GameMode.Id);
        _clock.Advance(TimeSpan.FromMinutes(5));
        engine.Revert(Mouse.Id);

        var history = engine.ListHistory();

        Assert.Collection(history,
            change =>
            {
                Assert.Equal("Turn on Game Mode", change.TweakName);
                Assert.Equal(Start.AddMinutes(5), change.ChangedAt);
                Assert.Equal(ChangeState.Applied, change.State);
                Assert.True(change.CanUndo);
            },
            change =>
            {
                Assert.Equal("Turn off Enhance pointer precision", change.TweakName);
                Assert.Equal(Start, change.ChangedAt);
                Assert.Equal(ChangeState.Undone, change.State);
                Assert.Equal(Start.AddMinutes(10), change.UndoneAt);
                Assert.False(change.CanUndo);
            });
    }

    [Fact]
    public void Undoing_one_change_from_the_history_puts_back_only_that_change()
    {
        SeedOtherValues();
        var mouseBefore = RegistrySnapshot.Take(_registry, Mouse);
        var engine = NewEngine();
        engine.Apply(Mouse.Id);
        engine.Apply(GameMode.Id);
        var gameModeApplied = RegistrySnapshot.Take(_registry, GameMode);
        var mouseChange = engine.ListHistory().Single(c => c.TweakId == Mouse.Id);

        var result = engine.UndoChange(mouseChange.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(mouseBefore, RegistrySnapshot.Take(_registry, Mouse));
        Assert.Equal(gameModeApplied, RegistrySnapshot.Take(_registry, GameMode));
        Assert.Equal(ChangeState.Undone, engine.ListHistory().Single(c => c.Id == mouseChange.Id).State);
        Assert.Equal(TweakState.Off, engine.ListTweaks().Single(t => t.Id == Mouse.Id).State);
    }

    [Fact]
    public void A_change_that_was_already_undone_or_belongs_to_someone_else_cannot_be_undone()
    {
        var engine = NewEngine();
        engine.Apply(Mouse.Id);
        var change = engine.ListHistory().Single();
        engine.UndoChange(change.Id);

        Assert.False(engine.UndoChange(change.Id).Succeeded);
        Assert.False(engine.UndoChange(Guid.NewGuid()).Succeeded);

        engine.Apply(Mouse.Id);
        var mine = engine.ListHistory().First();
        Assert.False(NewEngine("S-1-5-21-someone-else").UndoChange(mine.Id).Succeeded);
        Assert.Equal(TweakState.On, engine.ListTweaks().Single(t => t.Id == Mouse.Id).State);
    }

    [Fact]
    public void Restore_everything_puts_back_every_change_newest_first()
    {
        // A second tweak that writes one of the same values as the mouse tweak: only undoing newest first
        // brings back the user's own value.
        var mouseSpeed = ((RegistryValueStep)Mouse.Steps[0]).Location;
        var fastMouse = new TweakDefinition(
            "test.fast-mouse", Sections.Gaming, "Fast mouse", "Test only.", "None.", [], EvidenceTier.Proven, "Puts it back.",
            [new RegistryValueStep(mouseSpeed, RegistryValue.String("2"))]);
        var catalogue = new Catalogue([Mouse, GameMode, fastMouse]);
        SeedOtherValues();
        var before = Snapshot(catalogue);
        var engine = NewEngine(catalogue: catalogue);
        engine.Apply(Mouse.Id);
        engine.Apply(GameMode.Id);
        engine.Apply(fastMouse.Id);

        var result = engine.RestoreEverything();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(before, Snapshot(catalogue));
        Assert.All(engine.ListHistory(), change => Assert.Equal(ChangeState.Undone, change.State));
        Assert.Equal(["Fast mouse", "Turn on Game Mode", "Turn off Enhance pointer precision"], result.Changes.Select(c => c.TweakName));
        Assert.All(result.Changes, c => Assert.True(c.Succeeded, c.Message));
    }

    [Fact]
    public void Restore_everything_with_nothing_to_undo_says_so()
    {
        var result = NewEngine().RestoreEverything();

        Assert.True(result.Succeeded);
        Assert.Empty(result.Changes);
        Assert.Contains("Nothing to undo", result.Message);
    }

    [Fact]
    public void Undo_refuses_to_overwrite_a_value_something_else_changed_since_and_says_so()
    {
        SeedOtherValues();
        var engine = NewEngine();
        engine.Apply(Mouse.Id);
        var threshold = ((RegistryValueStep)Mouse.Steps[1]).Location;
        _registry.SetValue(threshold, RegistryValue.String("5"));
        var changedByOthers = RegistrySnapshot.Take(_registry, Mouse);
        var change = engine.ListHistory().Single();

        foreach (var result in new[] { engine.UndoChange(change.Id), engine.Revert(Mouse.Id) })
        {
            Assert.False(result.Succeeded);
            Assert.Contains(@"HKCU\Control Panel\Mouse\MouseThreshold1", result.Message);
            Assert.Contains("changed", result.Message);
            Assert.Contains("\"5\" (REG_SZ)", result.Message);
        }

        // Nothing at all was touched, not even the values that still match.
        Assert.Equal(changedByOthers, RegistrySnapshot.Take(_registry, Mouse));
        Assert.True(engine.ListHistory().Single().CanUndo);
    }

    [Fact]
    public void Restore_everything_skips_a_conflicting_change_reports_it_and_restores_the_rest()
    {
        SeedOtherValues();
        var gameModeBefore = RegistrySnapshot.Take(_registry, GameMode);
        var engine = NewEngine();
        engine.Apply(Mouse.Id);
        engine.Apply(GameMode.Id);
        _registry.SetValue(((RegistryValueStep)Mouse.Steps[0]).Location, RegistryValue.String("7"));
        var mouseChangedByOthers = RegistrySnapshot.Take(_registry, Mouse);

        var result = engine.RestoreEverything();

        Assert.False(result.Succeeded);
        Assert.Contains("1 of 2", result.Message);
        Assert.Equal(gameModeBefore, RegistrySnapshot.Take(_registry, GameMode));
        Assert.Equal(mouseChangedByOthers, RegistrySnapshot.Take(_registry, Mouse));
        var blocked = Assert.Single(result.Changes, c => !c.Succeeded);
        Assert.Equal(Mouse.Name, blocked.TweakName);
        Assert.Contains("MouseSpeed", blocked.Message);
    }

    private IReadOnlyList<string> Snapshot(Catalogue catalogue) =>
        catalogue.Tweaks.SelectMany(t => RegistrySnapshot.Take(_registry, t)).ToList();

    /// <summary>Every value the tweaks touch starts with a value the user chose, different from what the app writes.</summary>
    private void SeedOtherValues()
    {
        foreach (var step in _catalogue.Tweaks.SelectMany(t => t.Steps).OfType<RegistryValueStep>())
            _registry.SetValue(step.Location, new RegistryValue(RegistryValueKind.Binary, [0xAB, 0xCD]));
    }

    private Engine NewEngine(string user = User, Catalogue? catalogue = null) =>
        new(catalogue ?? _catalogue, new SystemAdapters(_registry, new FakeSystemInfo()), _journal, user, clock: _clock);
}
