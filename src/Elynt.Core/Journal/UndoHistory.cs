using Elynt.Core.Profiles;
using Elynt.Core.Safety;
using Elynt.Core.Services;
using Elynt.Core.Startup;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Logging;

namespace Elynt.Core.Journal;

/// <summary>What a change in the Undo history was made to. Each kind names its changes its own way in the journal.</summary>
internal enum ChangeKind
{
    /// <summary>A catalogue tweak, saved under its id.</summary>
    Tweak,

    /// <summary>One game's copy of a per-game tweak, saved as <c>template-id|C:\path\game.exe</c>.</summary>
    PerGameTweak,

    /// <summary>A service's start type, saved as <c>service:name</c>.</summary>
    Service,

    /// <summary>A background app a per-game profile closed, saved as <c>profile.close-app|C:\path\app.exe</c>.</summary>
    ClosedApp,

    /// <summary>A startup item switched on or off, saved as <c>startup:item-id</c>.</summary>
    StartupSwitch,
}

/// <summary>What came of making, re-applying or undoing one change, in the words the user reads.</summary>
internal sealed record ChangeOutcome(bool Succeeded, string Message)
{
    /// <summary>The tweak behind the change, when this version still has it. A startup switch has none.</summary>
    public TweakDefinition? Tweak { get; init; }

    /// <summary>Undo refused because something else has changed the setting since the app's change.</summary>
    public bool ChangedSince { get; init; }

    /// <summary>Why the change's parts can't be read or made on this PC. Everything was put back.</summary>
    public string? NotPossibleHere { get; init; }
}

/// <summary>
/// One account's Undo history: its own changes and every whole-PC change, from any administrator account (ADR 0007),
/// over the journal all accounts share. It owns the whole life of a change: making it safely (the safety net, then
/// the exact prior state saved before anything changes, put back if the change fails), undoing it (refused when
/// something else has changed the setting since, ADR 0002), re-applying it after drift, and keeping a game profile's
/// temporary change. Nothing else writes to the journal. Whether a change is allowed at all is the Engine's to decide.
/// </summary>
internal sealed class UndoHistory(
    ChangeJournal journal,
    string user,
    Catalogue catalogue,
    SystemAdapters system,
    SafetyNet? safetyNet,
    TimeProvider clock,
    ILogger log)
{
    /// <summary>
    /// The newest change to this tweak that hasn't been undone, or null. It is the only one that can be undone: older
    /// ones are what it was made over.
    /// </summary>
    public JournalEntry? Newest(string tweakId) => journal.LatestUndoable(tweakId, user);

    /// <summary>Whether this is the newest change to its tweak that hasn't been undone, so it can be undone.</summary>
    public bool IsNewest(JournalEntry change) => Newest(change.TweakId) == change;

    /// <summary>Every tweak, service, startup item or closed app with a change to undo, oldest first.</summary>
    public IReadOnlyList<string> WithChanges(ChangeKind kind) =>
        journal.UndoableTweakIds(user).Where(id => KindOf(id) == kind).ToList();

    /// <summary>Every tweak (per-game ones included), service and closed app with a change to undo, oldest first.</summary>
    public IReadOnlyList<string> TweaksWithChanges() =>
        journal.UndoableTweakIds(user).Where(id => KindOf(id) != ChangeKind.StartupSwitch).ToList();

    /// <summary>When this tweak was last changed or undone, or null if it never was.</summary>
    public DateTimeOffset? LastChangedAt(string tweakId) => journal.LastChangedAt(tweakId, user);

    /// <summary>This account's temporary changes still in place, oldest first.</summary>
    public IReadOnlyList<JournalEntry> Temporary() => journal.OpenTemporary(user);

    /// <summary>The Undo history as the page shows it, newest first.</summary>
    public IReadOnlyList<ChangeView> List() => journal.EntriesFor(user)
        .Select(change => new ChangeView(
            change.Id,
            change.TweakId,
            NameOf(change),
            change.AppliedAt,
            change.Status switch
            {
                EntryStatus.Applying => ChangeState.Interrupted,
                EntryStatus.Applied => ChangeState.Applied,
                EntryStatus.RolledBack => ChangeState.Failed,
                _ => ChangeState.Undone,
            },
            change.Status is EntryStatus.Reverted or EntryStatus.Superseded ? change.ClosedAt : null,
            IsNewest(change) && !IsDestructive(change))
        {
            Destructive = IsDestructive(change),
        })
        .Reverse()
        .ToList();

    /// <summary>What a saved change was made to, read from how its id is written.</summary>
    public static ChangeKind KindOf(string tweakId) =>
        tweakId.StartsWith(StartupManager.JournalPrefix, StringComparison.Ordinal) ? ChangeKind.StartupSwitch
        : tweakId.StartsWith(ServiceChange.IdPrefix, StringComparison.Ordinal) ? ChangeKind.Service
        : tweakId.StartsWith(CloseApp.IdPrefix, StringComparison.Ordinal) ? ChangeKind.ClosedApp
        : tweakId.Contains(PerGameTweak.Separator) ? ChangeKind.PerGameTweak
        : ChangeKind.Tweak;

    /// <summary>
    /// The tweak behind a change: a catalogue or per-game tweak, or one built from the saved change (a service's start
    /// type, a closed app). Null for a startup switch, and for a tweak this version no longer has.
    /// </summary>
    public TweakDefinition? Resolve(string tweakId, JournalEntry? change) => KindOf(tweakId) switch
    {
        ChangeKind.StartupSwitch => null,
        ChangeKind.Service => ServiceChange.Resolve(tweakId, change, system),
        ChangeKind.ClosedApp => CloseApp.Resolve(tweakId, change),
        _ => catalogue.TryGet(tweakId, out var tweak) ? tweak : null,
    };

    /// <summary>The name the Undo history shows for a change.</summary>
    public string NameOf(JournalEntry change) =>
        KindOf(change.TweakId) == ChangeKind.StartupSwitch ? StartupManager.NameFor(system, change.TweakId)
        : Resolve(change.TweakId, change)?.Name ?? change.TweakId;

    /// <summary>
    /// Made by a destructive tweak, so there is nothing to put back and it is never offered for undo (ADR 0002). Only
    /// catalogue tweaks are ever destructive; startup switches, service start types and closed apps never are.
    /// </summary>
    public bool IsDestructive(JournalEntry change) => catalogue.TryGet(change.TweakId, out var tweak) && IsDestructive(tweak);

    private static bool IsDestructive(TweakDefinition tweak) => tweak.Reversibility == Reversibility.Destructive;

    /// <summary>
    /// Makes a change through the safety net and the write-ahead. The Engine has already decided it is allowed.
    /// </summary>
    /// <param name="temporary">Made by a per-game profile, so undone when the game closes or after a crash or reboot.</param>
    /// <param name="replacing">An older change to the same thing that this one takes the place of; it is closed once this one is made.</param>
    public ChangeOutcome Make(TweakDefinition tweak, ChangeWording wording, bool temporary, JournalEntry? replacing = null)
    {
        // The safety net goes down before anything changes: whole-store backups, then a restore point.
        string? note;
        try
        {
            note = safetyNet?.BeforeChange(tweak);
        }
        catch (BackupFailedException ex)
        {
            return new(false, $"{ex.Message} The app backs up the whole store before its first change of this kind. Nothing was changed.");
        }

        var (result, error) = WriteAhead(tweak, [.. tweak.Steps.Select(_ => (StepRecord?)null)], temporary, replacing);
        switch (result)
        {
            case WriteAheadResult.Made:
                log.LogInformation("Applied {Tweak} for {User}", tweak.Id, user);
                return new(true, note is null ? wording.Done : $"{wording.Done} {note}");
            case WriteAheadResult.CouldNotRead or WriteAheadResult.NotSaved:
                return new(false, $"Couldn't save the current settings before changing them: {error!.Message} Nothing was changed.");
            case WriteAheadResult.PutBack:
                return new(false, $"{wording.CouldNot}: {error!.Message} Nothing was changed.");
            default:
                return new(false, $"{wording.CouldNot}: {error!.Message} The prior state is saved; {wording.UndoHint}.");
        }
    }

    /// <summary>
    /// Puts back the parts of a change that something else undid. Those parts get a fresh before-state (what Windows
    /// set), so undo returns to what this build ships; the parts still in place keep the before-state from the
    /// original change, which this one replaces.
    /// </summary>
    /// <param name="drifted">For each part of the tweak, whether something else has undone it.</param>
    public ChangeOutcome Reapply(TweakDefinition tweak, JournalEntry previous, IReadOnlyList<bool> drifted)
    {
        try
        {
            safetyNet?.BeforeChange(tweak);
        }
        catch (BackupFailedException ex)
        {
            return new(false, $"{ex.Message} Nothing was changed.");
        }

        var reuse = previous.Steps.Count == tweak.Steps.Count;
        var kept = tweak.Steps.Select((_, i) => reuse && !drifted[i] ? previous.Steps[i] : null).ToList();
        // A per-game profile's change stays temporary, so the game closing still undoes it.
        var (result, error) = WriteAhead(tweak, kept, previous.Temporary, replacing: previous);
        switch (result)
        {
            case WriteAheadResult.Made:
                log.LogInformation("Re-applied drifted {Tweak} for {User}", tweak.Id, user);
                return new(true, "Re-applied.");
            case WriteAheadResult.CouldNotRead or WriteAheadResult.PutBack:
                return new(false, $"Couldn't re-apply this: {error!.Message} Nothing was changed.") { NotPossibleHere = error.Message };
            case WriteAheadResult.NotSaved:
                return new(false, $"Couldn't save the current settings first: {error!.Message} Nothing was changed.");
            default:
                return new(false, $"Couldn't re-apply this: {error!.Message} The prior state is saved; undo it from Undo history.");
        }
    }

    /// <summary>
    /// When the newest change to this tweak is a game profile's temporary one, the user switching the tweak on makes it
    /// theirs: it stays after the game closes, and undo still goes back to how it was before the game.
    /// </summary>
    public bool KeepGameChange(string tweakId)
    {
        if (Newest(tweakId) is not { Temporary: true } change)
            return false;
        journal.KeepPermanently(change);
        log.LogInformation("Kept {Tweak} for {User} after the game closes", tweakId, user);
        return true;
    }

    /// <summary>
    /// Undoes one change from the Undo history: only the newest change to its tweak, never one already undone, and
    /// never a destructive one.
    /// </summary>
    public ChangeOutcome UndoChange(Guid changeId)
    {
        var change = journal.EntriesFor(user).FirstOrDefault(e => e.Id == changeId);
        if (change is null)
            return new(false, "There is no such change in your undo history.");

        var startupSwitch = KindOf(change.TweakId) == ChangeKind.StartupSwitch;
        var tweak = Resolve(change.TweakId, change);
        if (!startupSwitch && tweak is null)
            return new(false, $"This change was made by a tweak this version no longer has ('{change.TweakId}').");
        if (change.Status is not (EntryStatus.Applying or EntryStatus.Applied))
            return new(false, "This change has already been undone.") { Tweak = tweak };
        if (!IsNewest(change))
            return new(false, startupSwitch
                ? "A newer change to this startup item comes after this one. Undo the newer change instead."
                : "A newer change to this tweak comes after this one. Undo the newer change instead.") { Tweak = tweak };

        return Undo(change);
    }

    /// <summary>
    /// Puts back exactly what was there before a change, unless something else has changed the setting since, and
    /// closes every older change to the same tweak, so a second undo can never write back a stale snapshot.
    /// </summary>
    /// <param name="wording">How the page that asked words it; a tweak's, or a startup switch's, by default.</param>
    public ChangeOutcome Undo(JournalEntry change, ChangeWording? wording = null)
    {
        var tweak = Resolve(change.TweakId, change);
        wording ??= KindOf(change.TweakId) == ChangeKind.StartupSwitch ? ChangeWording.StartupSwitch : ChangeWording.Tweak;
        // Refused before anything is read or written: a destructive change has nothing to put back (ADR 0002).
        if (tweak is not null && IsDestructive(tweak))
            return new(false, $"This can't be undone. {tweak.DestructiveWarning} Nothing was changed.") { Tweak = tweak };

        try
        {
            var conflicts = change.Steps.Select(step => step.FindConflict(system)).OfType<string>().ToList();
            if (conflicts.Count > 0)
            {
                log.LogWarning("Not undoing {Tweak} for {User}: {Conflicts}", change.TweakId, user, conflicts);
                var takeAFreshCopy = wording.ChangeAgain is { } again
                    ? $" {again} to take a fresh copy of the current settings, or leave it as it is."
                    : "";
                return new(false, $"Didn't undo this. {string.Join(" ", conflicts)} Undoing now would overwrite that, so nothing was changed.{takeAFreshCopy}")
                {
                    Tweak = tweak,
                    ChangedSince = true,
                };
            }

            UndoSteps(change.Steps);
            journal.CloseUndone(change, user, clock.GetUtcNow());
            log.LogInformation("Undid {Tweak} for {User}", change.TweakId, user);
            return new(true, wording.Undone) { Tweak = tweak };
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Undoing {Tweak} failed", change.TweakId);
            return new(false, $"{wording.CouldNotUndo}: {ex.Message}") { Tweak = tweak };
        }
    }

    /// <summary>
    /// Undoes every change in the Undo history that can be undone, newest first. A change whose undo fails is reported
    /// and skipped; destructive changes are never tried, only named (ADR 0002).
    /// </summary>
    public RestoreResult RestoreEverything()
    {
        // Journal order is the true order of changes (clocks can jump), so newest first is the journal reversed.
        var open = journal.EntriesFor(user).Where(IsNewest).Reverse().ToList();
        var left = open.Where(IsDestructive).Select(NameOf).ToList();

        var results = new List<UndoOutcome>();
        foreach (var change in open.Where(change => !IsDestructive(change)))
        {
            var name = NameOf(change);
            var undone = UndoChange(change.Id);
            results.Add(new UndoOutcome(change.Id, name, undone.Succeeded, undone.Message));
        }

        var leftAlone = left.Count switch
        {
            0 => "",
            1 => $" 1 change was left as it is, because it can never be undone: {left[0]}.",
            _ => $" {left.Count} changes were left as they are, because they can never be undone: {string.Join(", ", left)}.",
        };
        if (results.Count == 0)
            return new RestoreResult(true, left.Count == 0
                ? "Nothing to undo: every change the app made has already been put back."
                : $"Nothing to undo.{leftAlone}", results);

        var failed = results.Count(r => !r.Succeeded);
        log.LogInformation("Restore everything for {User}: {Undone} undone, {Failed} not, {Left} left because they can't be undone",
            user, results.Count - failed, failed, left.Count);
        var undoable = results.Count == 1 ? "1 change" : $"{results.Count} changes";
        return failed == 0
            ? new RestoreResult(true, left.Count == 0
                ? $"Put back all {results.Count} changes. Your previous settings are back."
                : $"Put back the {undoable} that can be undone.{leftAlone}", results)
            : new RestoreResult(false, $"Put back {results.Count - failed} of {results.Count} changes. {failed} couldn't be undone; see each one below.{leftAlone}", results);
    }

    private enum WriteAheadResult
    {
        Made,

        /// <summary>A part's current state couldn't be read. Nothing was changed.</summary>
        CouldNotRead,

        /// <summary>The prior state couldn't be saved. Nothing was changed.</summary>
        NotSaved,

        /// <summary>A part failed, and the parts made so far were put back.</summary>
        PutBack,

        /// <summary>A part failed and putting it back failed too, so the change stays open as an interrupted change.</summary>
        Interrupted,
    }

    /// <summary>
    /// The write-ahead: the prior state of every part to change is saved before anything changes, so undo works even
    /// after a crash. If a part fails, the parts made so far are put back. If that fails too, the change stays open,
    /// an interrupted change, and can be undone like any other.
    /// </summary>
    /// <param name="kept">For each part, its saved record when it is still in place from an earlier change and isn't made again.</param>
    private (WriteAheadResult Result, Exception? Error) WriteAhead(
        TweakDefinition tweak, IReadOnlyList<StepRecord?> kept, bool temporary, JournalEntry? replacing)
    {
        var records = new List<StepRecord>();
        var fresh = new List<StepRecord>();
        try
        {
            for (var i = 0; i < tweak.Steps.Count; i++)
            {
                if (kept[i] is { } stillInPlace)
                {
                    records.Add(stillInPlace);
                    continue;
                }
                var record = tweak.Steps[i].Capture(system);
                fresh.Add(record);
                records.Add(record);
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Couldn't read the current state of {Tweak}", tweak.Id);
            return (WriteAheadResult.CouldNotRead, ex);
        }

        JournalEntry change;
        try
        {
            change = journal.Open(tweak.Id, user, system.System.OsBuild, clock.GetUtcNow(), records, temporary);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Couldn't save the prior state of {Tweak}", tweak.Id);
            return (WriteAheadResult.NotSaved, ex);
        }

        try
        {
            for (var i = 0; i < tweak.Steps.Count; i++)
                if (kept[i] is null)
                    tweak.Steps[i].Apply(system);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Making {Tweak} failed; putting back the prior state", tweak.Id);
            try
            {
                UndoSteps(fresh);
                journal.Close(change, EntryStatus.RolledBack, clock.GetUtcNow());
                return (WriteAheadResult.PutBack, ex);
            }
            catch (Exception putBack)
            {
                log.LogError(putBack, "Couldn't put back the prior state of {Tweak}; it stays in the Undo history to undo", tweak.Id);
                return (WriteAheadResult.Interrupted, ex);
            }
        }

        try
        {
            journal.Close(change, EntryStatus.Applied, clock.GetUtcNow());
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Couldn't record {Tweak} as made; it stays in the Undo history to undo", tweak.Id);
            return (WriteAheadResult.Interrupted, ex);
        }

        if (replacing is not null)
        {
            try
            {
                journal.Supersede(replacing, clock.GetUtcNow());
            }
            catch (Exception ex)
            {
                // Harmless: undoing the new change closes the older one too.
                log.LogWarning(ex, "Couldn't close the older change to {Tweak} it replaces", tweak.Id);
            }
        }
        return (WriteAheadResult.Made, null);
    }

    private void UndoSteps(IReadOnlyList<StepRecord> records)
    {
        for (var i = records.Count - 1; i >= 0; i--)
            records[i].Undo(system);
    }
}
