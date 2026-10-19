using Elynt.Core.Drift;
using Elynt.Core.Journal;
using Elynt.Core.Safety;
using Elynt.Core.Startup;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Logging;

namespace Elynt.Core;

/// <summary>The drift commands.</summary>
public partial interface IEngine
{
    /// <summary>
    /// Detects every change the app made and lists those something else (usually a Windows update) undid, including
    /// removed apps that came back. Full when the Windows build changed since the last check, quick otherwise. With
    /// automatic re-apply on, instant changes are put back during the check.
    /// </summary>
    DriftReport CheckDrift();

    /// <summary>Re-applies the named drifted changes, or all of them when <paramref name="tweakIds"/> is null.</summary>
    /// <param name="confirmDestructive">The user confirmed, in a separate step, re-applying destructive changes.</param>
    ReapplyResult ReapplyDrift(IReadOnlyList<string>? tweakIds, bool confirmDestructive = false);

    DriftSettings GetDriftSettings();

    /// <summary>Turns automatic re-apply on or off. Off by default; never for destructive or reboot-needing changes.</summary>
    DriftSettings SetAutoReapply(bool enabled);
}

/// <summary>
/// The drift monitor: finds changes the app made that something else (usually a Windows update) has since undone,
/// including removed apps that came back, and puts them back when the user asks (or automatically, if they chose
/// that, for instant changes only).
/// </summary>
public sealed partial class Engine
{
    private readonly IDriftStore _drift = driftStore ?? new InMemoryDriftStore();

    /// <summary>Marks last until the Windows build or the app itself changes.</summary>
    private static readonly string AppVersion = typeof(Engine).Assembly.GetName().Version?.ToString() ?? "0";

    /// <summary>
    /// Detects every applied change. The first check, and the first after the Windows build changed, is a full
    /// check: marks from other builds or app versions are dropped, so those changes are looked at afresh.
    /// </summary>
    public DriftReport CheckDrift()
    {
        var state = _drift.Load(user);
        var build = system.System.OsBuild;
        var kind = state.LastBuild == build ? DriftCheckKind.Quick : DriftCheckKind.Full;
        _drift.Save(user, state with { LastBuild = build, Unsupported = CurrentMarks(state) });

        var drifted = new List<DriftItem>();
        var reapplied = new List<DriftItem>();
        var unsupported = new List<UnsupportedChange>();
        foreach (var (tweak, entry) in AppliedChanges())
        {
            DriftItem? item;
            try
            {
                if (WhyNotApplicable(tweak) is { } reason)
                {
                    unsupported.Add(MarkUnsupported(tweak, reason));
                    continue;
                }
                item = Drifted(tweak);
            }
            catch (Exception ex)
            {
                // The setting can't even be read on this build: its target is gone.
                _log.LogWarning(ex, "Couldn't read the state of {Tweak} while checking for drift", tweak.Id);
                unsupported.Add(MarkUnsupported(tweak, ex.Message));
                continue;
            }
            if (item is null)
                continue;

            if (state.AutoReapply && MayReapplyAutomatically(tweak))
            {
                var outcome = Reapply(tweak, entry, confirmDestructive: false);
                if (outcome is { Succeeded: true })
                {
                    reapplied.Add(item);
                    continue;
                }
                if (IsMarked(tweak.Id))
                {
                    unsupported.Add(new UnsupportedChange(tweak.Id, tweak.Name, outcome?.Message ?? ""));
                    continue;
                }
            }
            drifted.Add(item);
        }

        _log.LogInformation("{Kind} drift check for {User} on {Build}: {Drifted} drifted, {Reapplied} re-applied, {Unsupported} unsupported",
            kind, user, build, drifted.Count, reapplied.Count, unsupported.Count);
        return new DriftReport(kind, build, drifted, reapplied, unsupported, Describe(drifted, reapplied, unsupported));
    }

    /// <summary>
    /// Re-applies the named drifted changes, or every one when <paramref name="tweakIds"/> is null. Changes not
    /// supported on this build are skipped quietly; destructive ones need <paramref name="confirmDestructive"/>.
    /// </summary>
    public ReapplyResult ReapplyDrift(IReadOnlyList<string>? tweakIds, bool confirmDestructive = false)
    {
        var wanted = tweakIds?.ToHashSet(StringComparer.Ordinal);
        var outcomes = AppliedChanges()
            .Where(change => wanted is null || wanted.Contains(change.Tweak.Id))
            .Where(change => !IsMarked(change.Tweak.Id))
            .Select(change => Reapply(change.Tweak, change.Entry, confirmDestructive))
            .OfType<ReapplyOutcome>()
            .ToList();

        if (outcomes.Count == 0)
            return new ReapplyResult(true, "Nothing to re-apply: every change the app made is still in place.", outcomes);
        var failed = outcomes.Count(o => !o.Succeeded);
        return failed == 0
            ? new ReapplyResult(true, outcomes.Count == 1 ? "Re-applied 1 change." : $"Re-applied all {outcomes.Count} changes.", outcomes)
            : new ReapplyResult(false, $"Re-applied {outcomes.Count - failed} of {outcomes.Count} changes. {failed} weren't; see each one below.", outcomes);
    }

    public DriftSettings GetDriftSettings() => new(_drift.Load(user).AutoReapply);

    /// <summary>Turns automatic re-apply on or off. Off by default.</summary>
    public DriftSettings SetAutoReapply(bool enabled)
    {
        _drift.Save(user, _drift.Load(user) with { AutoReapply = enabled });
        _log.LogInformation("Automatic re-apply after drift turned {State} for {User}", enabled ? "on" : "off", user);
        return GetDriftSettings();
    }

    /// <summary>Destructive, reboot-needing and must-read changes always ask first.</summary>
    private static bool MayReapplyAutomatically(TweakDefinition tweak) =>
        tweak.Reversibility != Reversibility.Destructive &&
        !tweak.Risks.HasFlag(RiskFlags.NeedsReboot) &&
        tweak.ApplyWarning is null;

    /// <summary>The parts of a tweak that are no longer in place, or null when all of it still is.</summary>
    private DriftItem? Drifted(TweakDefinition tweak)
    {
        var undone = tweak.Steps.Where(step => !step.IsApplied(system)).ToList();
        if (undone.Count == 0)
            return null;
        return new DriftItem(
            tweak.Id,
            tweak.Name,
            undone.Any(step => step is AppRemovalStep) ? DriftKind.AppReturned : DriftKind.SettingChanged,
            undone.Select(step => step.DescribeChange()).ToList(),
            tweak.Reversibility == Reversibility.Destructive,
            tweak.Risks.HasFlag(RiskFlags.NeedsReboot));
    }

    /// <summary>
    /// Puts back the parts of a change that something else undid. Those parts get a fresh before-state (what
    /// Windows set), so undo returns to what this build ships; the parts left alone keep the before-state from the
    /// original change. Null when nothing drifted.
    /// </summary>
    private ReapplyOutcome? Reapply(TweakDefinition tweak, JournalEntry previous, bool confirmDestructive)
    {
        List<bool> undone;
        try
        {
            undone = tweak.Steps.Select(step => !step.IsApplied(system)).ToList();
        }
        catch (Exception ex)
        {
            return Unsupported(tweak, ex);
        }
        if (!undone.Contains(true))
            return null;

        // The user confirmed any must-read warning when they first applied it.
        if (Refusal(tweak, new ApplyOptions(ConfirmDestructive: confirmDestructive, ConfirmWarning: true)) is { } refused)
            return new ReapplyOutcome(tweak.Id, tweak.Name, false, refused.Message);

        try
        {
            safetyNet?.BeforeChange(tweak);
        }
        catch (BackupFailedException ex)
        {
            return new ReapplyOutcome(tweak.Id, tweak.Name, false, $"{ex.Message} Nothing was changed.");
        }

        var reuse = previous.Steps.Count == tweak.Steps.Count;
        List<StepRecord> fresh = [];
        List<StepRecord> records = [];
        JournalEntry entry;
        try
        {
            for (var i = 0; i < tweak.Steps.Count; i++)
            {
                if (reuse && !undone[i])
                {
                    records.Add(previous.Steps[i]);
                    continue;
                }
                var record = tweak.Steps[i].Capture(system);
                fresh.Add(record);
                records.Add(record);
            }
        }
        catch (Exception ex)
        {
            return Unsupported(tweak, ex);
        }

        try
        {
            // A per-game profile's change stays temporary, so the game closing still undoes it.
            entry = journal.Open(tweak.Id, user, system.System.OsBuild, _clock.GetUtcNow(), records, previous.Temporary);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Couldn't record the prior state of {Tweak} before re-applying it", tweak.Id);
            return new ReapplyOutcome(tweak.Id, tweak.Name, false, $"Couldn't save the current settings first: {ex.Message} Nothing was changed.");
        }

        try
        {
            for (var i = 0; i < tweak.Steps.Count; i++)
                if (!reuse || undone[i])
                    tweak.Steps[i].Apply(system);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Re-applying {Tweak} failed; putting back what Windows set", tweak.Id);
            try
            {
                RevertSteps(fresh);
            }
            catch (Exception rollback)
            {
                _log.LogError(rollback, "Couldn't put back what Windows set for {Tweak}", tweak.Id);
            }
            journal.Close(entry, EntryStatus.RolledBack, _clock.GetUtcNow());
            return Unsupported(tweak, ex);
        }

        journal.Close(entry, EntryStatus.Applied, _clock.GetUtcNow());
        journal.Supersede(previous, _clock.GetUtcNow());
        _log.LogInformation("Re-applied drifted {Tweak} for {User}", tweak.Id, user);
        return new ReapplyOutcome(tweak.Id, tweak.Name, true, "Re-applied.");
    }

    /// <summary>A change that can't be made on this build is reported once, then skipped quietly.</summary>
    private ReapplyOutcome Unsupported(TweakDefinition tweak, Exception ex)
    {
        MarkUnsupported(tweak, ex.Message);
        return new ReapplyOutcome(tweak.Id, tweak.Name, false,
            $"Couldn't re-apply this: {ex.Message} It's marked not supported on this Windows build and skipped until Windows or the app updates.");
    }

    private UnsupportedChange MarkUnsupported(TweakDefinition tweak, string reason)
    {
        var state = _drift.Load(user);
        var marks = CurrentMarks(state);
        marks[tweak.Id] = new UnsupportedMark(system.System.OsBuild, AppVersion, reason);
        _drift.Save(user, state with { Unsupported = marks });
        _log.LogInformation("Marked {Tweak} not supported on build {Build}: {Reason}", tweak.Id, system.System.OsBuild, reason);
        return new UnsupportedChange(tweak.Id, tweak.Name, reason);
    }

    /// <summary>Marks made on this Windows build by this app version; the rest no longer hold.</summary>
    private Dictionary<string, UnsupportedMark> CurrentMarks(DriftState state) => state.Unsupported
        .Where(pair => pair.Value.OsBuild == system.System.OsBuild && pair.Value.AppVersion == AppVersion)
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private bool IsMarked(string tweakId) => UnsupportedReason(tweakId) is not null;

    /// <summary>Why this tweak was marked not supported on this build, or null when it wasn't.</summary>
    private string? UnsupportedReason(string tweakId) =>
        _drift.Load(user).Unsupported.TryGetValue(tweakId, out var mark) &&
        mark.OsBuild == system.System.OsBuild && mark.AppVersion == AppVersion
            ? $"Not supported on this Windows build ({mark.OsBuild}): {mark.Reason}"
            : null;

    private static string Describe(
        IReadOnlyList<DriftItem> drifted, IReadOnlyList<DriftItem> reapplied, IReadOnlyList<UnsupportedChange> unsupported)
    {
        var parts = new List<string>();
        if (drifted.Count > 0)
        {
            var names = string.Join(", ", drifted.Select(d => d.Name));
            parts.Add(drifted.Count == 1
                ? $"Windows undid 1 change: {names}. Re-apply it?"
                : $"Windows undid {drifted.Count} changes: {names}. Re-apply them all, or pick which.");
        }
        if (reapplied.Count > 0)
            parts.Add($"Put back automatically: {string.Join(", ", reapplied.Select(d => d.Name))}.");
        if (unsupported.Count > 0)
            parts.Add($"Not supported on this Windows build, so skipped: {string.Join(", ", unsupported.Select(u => u.Name))}.");
        return parts.Count == 0 ? "Everything the app changed is still in place." : string.Join(" ", parts);
    }

    /// <summary>
    /// Every tweak whose newest change is fully applied, with that change. Startup switches aren't tweaks, and a
    /// change that ends on its own (the update pause) isn't drift when it ends.
    /// </summary>
    private IEnumerable<(TweakDefinition Tweak, JournalEntry Entry)> AppliedChanges()
    {
        foreach (var id in journal.UndoableTweakIds(user))
        {
            if (StartupManager.Owns(id))
                continue;
            var entry = journal.LatestUndoable(id, user);
            if (entry is not { Status: EntryStatus.Applied } || !TryResolve(id, entry, out var tweak) || IsMarked(id) || tweak.Expires)
                continue;
            yield return (tweak, entry);
        }
    }
}
