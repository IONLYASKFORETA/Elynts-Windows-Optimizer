using Elynt.Core.Apps;
using Elynt.Core.Bios;
using Elynt.Core.Journal;
using Elynt.Core.Safety;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elynt.Core;

public enum TweakState
{
    Off,
    On,
}

/// <summary>A tweak as the UI shows it: its description plus its real, detected state.</summary>
public sealed record TweakView(
    string Id,
    string Section,
    string Name,
    string WhatItDoes,
    string Evidence,
    IReadOnlyList<Source> Sources,
    EvidenceTier Tier,
    IReadOnlyList<string> ExactChange,
    string Undo,
    SessionRefresh Refresh,
    TweakState State,
    bool CanUndo)
{
    public Reversibility Reversibility { get; init; } = Reversibility.Reversible;

    /// <summary>For a destructive tweak: what cannot be undone, shown in the confirmation.</summary>
    public string? DestructiveWarning { get; init; }

    public RiskFlags Risks { get; init; } = RiskFlags.None;

    public IReadOnlyList<AntiCheatConflict> AntiCheat { get; init; } = [];

    /// <summary>The tweak needs a reboot and was applied or undone since Windows last started.</summary>
    public bool PendingReboot { get; init; }

    /// <summary>Why the tweak doesn't fit this PC's hardware, edition or build, or null when it does.</summary>
    public string? NotApplicableReason { get; init; }

    public bool Applicable => NotApplicableReason is null;

    /// <summary>Instant, so a per-game profile may include it.</summary>
    public bool ProfileEligible { get; init; }
}

/// <summary>How the user asked for a tweak to be applied.</summary>
/// <param name="ConfirmDestructive">The user confirmed, in a separate step, that a destructive change can't be undone.</param>
/// <param name="Advanced">Advanced mode is on. It unlocks more, but never the hard never-touch rules.</param>
public sealed record ApplyOptions(bool ConfirmDestructive = false, bool Advanced = false)
{
    public static ApplyOptions Default { get; } = new();
}

public sealed record CommandResult(bool Succeeded, string Message, TweakView? Tweak);

/// <summary>Where one journaled change stands, as the Undo history page shows it.</summary>
public enum ChangeState
{
    Applied,
    /// <summary>The service stopped part-way through the change. What was saved before it can still be put back.</summary>
    Interrupted,
    /// <summary>The change failed part-way and was put back straight away.</summary>
    Failed,
    Undone,
}

/// <summary>One row of the Undo history: when, which tweak, and where it stands.</summary>
public sealed record ChangeView(
    Guid Id,
    string TweakId,
    string TweakName,
    DateTimeOffset ChangedAt,
    ChangeState State,
    DateTimeOffset? UndoneAt,
    bool CanUndo);

/// <summary>
/// The service's command surface. The pipe mirrors these commands one-to-one, and tests drive the app through
/// them, so this is the single test seam.
/// </summary>
public interface IEngine
{
    IReadOnlyList<TweakView> ListTweaks();
    CommandResult Apply(string tweakId, ApplyOptions? options = null);
    CommandResult Revert(string tweakId);

    /// <summary>Every change the app made for this user, newest first.</summary>
    IReadOnlyList<ChangeView> ListHistory();

    /// <summary>Undoes one change from the history, putting back exactly what was there before it.</summary>
    CommandResult UndoChange(Guid changeId);

    /// <summary>
    /// Undoes every change still in place, newest first. A change that can't be undone is reported and skipped;
    /// the rest still go ahead.
    /// </summary>
    RestoreResult RestoreEverything();

    IReadOnlyList<AppView> ListApps();

    /// <summary>
    /// Installs the ticked catalogue apps silently, one at a time, from winget's community source.
    /// <paramref name="allRuntimes"/> adds every Visual C++, DirectX legacy and .NET runtime to the queue.
    /// </summary>
    InstallReport InstallApps(IReadOnlyList<string> appIds, bool allRuntimes, IProgress<InstallProgress>? progress = null);

    BiosReport GetBiosReport();
}

/// <summary>What happened to one change during restore everything.</summary>
public sealed record UndoOutcome(Guid ChangeId, string TweakName, bool Succeeded, string Message);

public sealed record RestoreResult(bool Succeeded, string Message, IReadOnlyList<UndoOutcome> Changes);

/// <summary>Runs commands for one user, composing the catalogue, the journal and the system adapters.</summary>
public sealed class Engine(
    Catalogue catalogue,
    SystemAdapters system,
    ChangeJournal journal,
    string user,
    SafetyNet? safetyNet = null,
    TimeProvider? clock = null,
    ILogger<Engine>? logger = null,
    AppCatalogue? apps = null) : IEngine
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _log = logger ?? NullLogger<Engine>.Instance;
    private readonly AppInstaller _apps = new(apps ?? AppCatalogue.Default, system.Packages);

    public IReadOnlyList<AppView> ListApps() => _apps.ListApps();

    public InstallReport InstallApps(IReadOnlyList<string> appIds, bool allRuntimes, IProgress<InstallProgress>? progress = null) =>
        _apps.Install(appIds, allRuntimes, progress);

    /// <summary>
    /// Every tweak with its detected state, except tweaks that don't fit this PC and ask to be hidden. A hidden tweak
    /// still shows while it has a change to undo.
    /// </summary>
    public IReadOnlyList<TweakView> ListTweaks() => catalogue.Tweaks
        .Select(Describe)
        .Where(view => view.Applicable || view.CanUndo || !catalogue.Get(view.Id).Applicability.Hide)
        .ToList();

    public CommandResult Apply(string tweakId, ApplyOptions? options = null)
    {
        options ??= ApplyOptions.Default;
        if (!catalogue.TryGet(tweakId, out var tweak))
            return Unknown(tweakId);

        if (tweak.Applicability.WhyNot(system.System) is { } whyNot)
            return Failed(tweak, $"This doesn't apply to this PC. {whyNot} Nothing was changed.");

        // Hard rules hold in every mode, Advanced included.
        if (tweak.Steps.Select(step => HardRules.Violation(step.Target)).FirstOrDefault(v => v is not null) is { } violation)
        {
            _log.LogWarning("Refused {Tweak}: {Violation}", tweak.Id, violation);
            return Failed(tweak, $"This change is never allowed: {violation}. Nothing was changed.");
        }

        if (tweak.Reversibility == Reversibility.Destructive && !options.ConfirmDestructive)
            return Failed(tweak, $"This can't be undone, so it needs a separate confirmation first. {tweak.DestructiveWarning} Nothing was changed.");

        try
        {
            if (Detect(tweak) == TweakState.On)
                return Done(tweak, "Already on. Nothing was changed.");

            // The safety net goes down before anything changes: whole-store backups, then a restore point.
            string? note;
            try
            {
                note = safetyNet?.BeforeChange(tweak);
            }
            catch (BackupFailedException ex)
            {
                return Failed(tweak, $"{ex.Message} The app backs up the whole store before its first change of this kind. Nothing was changed.");
            }
            var turnedOn = note is null ? "Turned on." : $"Turned on. {note}";

            // Write-ahead: the exact prior state is on disk before anything changes.
            List<StepRecord> records;
            JournalEntry entry;
            try
            {
                records = tweak.Steps.Select(step => step.Capture(system)).ToList();
                entry = journal.Open(tweak.Id, user, system.System.OsBuild, _clock.GetUtcNow(), records);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Couldn't record the prior state of {Tweak}", tweak.Id);
                return Failed(tweak, $"Couldn't save the current settings before changing them: {ex.Message} Nothing was changed.");
            }

            try
            {
                foreach (var step in tweak.Steps)
                    step.Apply(system);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Applying {Tweak} failed; putting back the prior state", tweak.Id);
                RevertSteps(records);
                journal.Close(entry, EntryStatus.RolledBack, _clock.GetUtcNow());
                return Failed(tweak, $"Couldn't turn this on: {ex.Message} Nothing was changed.");
            }

            journal.Close(entry, EntryStatus.Applied, _clock.GetUtcNow());
            _log.LogInformation("Applied {Tweak} for {User}", tweak.Id, user);
            return Done(tweak, turnedOn);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Applying {Tweak} failed and could not be fully put back", tweak.Id);
            return Failed(tweak, $"Couldn't turn this on: {ex.Message} The prior state is saved; switch it off to undo.");
        }
    }

    public CommandResult Revert(string tweakId)
    {
        if (!catalogue.TryGet(tweakId, out var tweak))
            return Unknown(tweakId);

        var entry = journal.LatestUndoable(tweak.Id, user);
        if (entry is null)
            return Failed(tweak, "Nothing to undo: this was already set before the app changed anything.");

        return Undo(tweak, entry);
    }

    public CommandResult UndoChange(Guid changeId)
    {
        var entry = journal.EntriesFor(user).FirstOrDefault(e => e.Id == changeId);
        if (entry is null)
            return new(false, "There is no such change in your undo history.", null);
        if (!catalogue.TryGet(entry.TweakId, out var tweak))
            return new(false, $"This change was made by a tweak this version no longer has ('{entry.TweakId}').", null);
        if (entry.Status is not (EntryStatus.Applying or EntryStatus.Applied))
            return Failed(tweak, "This change has already been undone.");
        if (!IsUndoable(entry))
            return Failed(tweak, "A newer change to this tweak comes after this one. Undo the newer change instead.");

        return Undo(tweak, entry);
    }

    public RestoreResult RestoreEverything()
    {
        // Journal order is the true order of changes (clocks can jump), so newest first is the journal reversed.
        var open = journal.EntriesFor(user).Where(IsUndoable).Reverse().ToList();

        var results = new List<UndoOutcome>();
        foreach (var entry in open)
        {
            var name = catalogue.TryGet(entry.TweakId, out var tweak) ? tweak.Name : entry.TweakId;
            var result = UndoChange(entry.Id);
            results.Add(new UndoOutcome(entry.Id, name, result.Succeeded, result.Message));
        }

        if (results.Count == 0)
            return new RestoreResult(true, "Nothing to undo: every change the app made has already been put back.", results);

        var failed = results.Count(r => !r.Succeeded);
        _log.LogInformation("Restore everything for {User}: {Undone} undone, {Failed} not", user, results.Count - failed, failed);
        return failed == 0
            ? new RestoreResult(true, $"Put back all {results.Count} changes. Your previous settings are back.", results)
            : new RestoreResult(false, $"Put back {results.Count - failed} of {results.Count} changes. {failed} couldn't be undone; see each one below.", results);
    }

    private CommandResult Undo(TweakDefinition tweak, JournalEntry entry)
    {
        try
        {
            var conflicts = entry.Steps.Select(step => step.FindConflict(system)).OfType<string>().ToList();
            if (conflicts.Count > 0)
            {
                _log.LogWarning("Not reverting {Tweak} for {User}: {Conflicts}", tweak.Id, user, conflicts);
                return Failed(tweak,
                    $"Didn't undo this. {string.Join(" ", conflicts)} Undoing now would overwrite that, so nothing was changed. " +
                    "Switch the tweak on again to take a fresh copy of the current settings, or leave it as it is.");
            }

            RevertSteps(entry.Steps);
            journal.CloseReverted(entry, _clock.GetUtcNow());
            _log.LogInformation("Reverted {Tweak} for {User}", tweak.Id, user);
            return Done(tweak, "Turned off. The previous settings are back.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Reverting {Tweak} failed", tweak.Id);
            return Failed(tweak, $"Couldn't undo this: {ex.Message}");
        }
    }

    public IReadOnlyList<ChangeView> ListHistory()
    {
        var entries = journal.EntriesFor(user);
        return entries
            .Select(entry => new ChangeView(
                entry.Id,
                entry.TweakId,
                catalogue.TryGet(entry.TweakId, out var tweak) ? tweak.Name : entry.TweakId,
                entry.AppliedAt,
                entry.Status switch
                {
                    EntryStatus.Applying => ChangeState.Interrupted,
                    EntryStatus.Applied => ChangeState.Applied,
                    EntryStatus.RolledBack => ChangeState.Failed,
                    _ => ChangeState.Undone,
                },
                entry.Status is EntryStatus.Reverted or EntryStatus.Superseded ? entry.ClosedAt : null,
                IsUndoable(entry)))
            .Reverse()
            .ToList();
    }

    /// <summary>Only the newest open change to a tweak can be undone: older ones are what it was applied over.</summary>
    private bool IsUndoable(JournalEntry entry) => journal.LatestUndoable(entry.TweakId, entry.User) == entry;

    public BiosReport GetBiosReport()
    {
        HardwareSnapshot? hardware = null;
        try
        {
            hardware = system.Hardware?.Read();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't read the board, CPU and memory details");
        }

        return BiosAdvisor.Build(hardware);
    }

    private void RevertSteps(IReadOnlyList<StepRecord> records)
    {
        for (var i = records.Count - 1; i >= 0; i--)
            records[i].Revert(system);
    }

    private TweakState Detect(TweakDefinition tweak) =>
        tweak.Steps.All(step => step.IsApplied(system)) ? TweakState.On : TweakState.Off;

    private TweakView Describe(TweakDefinition tweak) => new(
        tweak.Id,
        tweak.Section,
        tweak.Name,
        tweak.WhatItDoes,
        tweak.Evidence,
        tweak.Sources,
        tweak.Tier,
        tweak.Steps.Select(step => step.DescribeChange()).ToList(),
        tweak.Undo,
        tweak.Refresh,
        Detect(tweak),
        journal.LatestUndoable(tweak.Id, user) is not null)
    {
        Reversibility = tweak.Reversibility,
        DestructiveWarning = tweak.DestructiveWarning,
        Risks = tweak.Risks,
        AntiCheat = tweak.AntiCheat,
        PendingReboot = tweak.Risks.HasFlag(RiskFlags.NeedsReboot) &&
                        journal.LastChangedAt(tweak.Id, user) > system.System.BootTime,
        NotApplicableReason = tweak.Applicability.WhyNot(system.System),
        ProfileEligible = tweak.ProfileEligible,
    };

    private CommandResult Done(TweakDefinition tweak, string message) => new(true, message, Describe(tweak));

    private CommandResult Failed(TweakDefinition tweak, string message) => new(false, message, SafeDescribe(tweak));

    private static CommandResult Unknown(string tweakId) => new(false, $"There is no tweak called '{tweakId}'.", null);

    private TweakView? SafeDescribe(TweakDefinition tweak)
    {
        try
        {
            return Describe(tweak);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't read the state of {Tweak}", tweak.Id);
            return null;
        }
    }
}
