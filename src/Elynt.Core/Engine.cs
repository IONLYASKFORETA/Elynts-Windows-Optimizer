using Elynt.Core.Bios;
using Elynt.Core.Drivers;
using Elynt.Core.Journal;
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

/// <summary>
/// The service's command surface. The pipe mirrors these commands one-to-one, and tests drive the app through
/// them, so this is the single test seam.
/// </summary>
public interface IEngine
{
    IReadOnlyList<TweakView> ListTweaks();
    CommandResult Apply(string tweakId, ApplyOptions? options = null);
    CommandResult Revert(string tweakId);
    BiosReport GetBiosReport();

    /// <summary>The detected GPUs and their installed driver versions.</summary>
    DriverReport GetDrivers();

    /// <summary>Looks up the latest driver for one detected GPU, falling back to the vendor's page.</summary>
    DriverCheck CheckDriver(DriverRequest request);

    /// <summary>
    /// Looks up, downloads, signature-checks and silently installs the latest driver for one detected GPU. A download
    /// that isn't signed by the vendor, or doesn't match the vendor's published hash, is deleted and never run.
    /// </summary>
    DriverInstallResult InstallDriver(DriverRequest request);
}

/// <summary>Runs commands for one user, composing the catalogue, the journal and the system adapters.</summary>
public sealed class Engine(
    Catalogue catalogue,
    SystemAdapters system,
    ChangeJournal journal,
    string user,
    TimeProvider? clock = null,
    ILogger<Engine>? logger = null) : IEngine
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _log = logger ?? NullLogger<Engine>.Instance;

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
            return Done(tweak, "Turned on.");
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

        try
        {
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

    public DriverReport GetDrivers() => DriverFlow.Report();

    public DriverCheck CheckDriver(DriverRequest request) => DriverFlow.Check(request);

    public DriverInstallResult InstallDriver(DriverRequest request) => DriverFlow.Install(request);

    private DriverFlow DriverFlow => new(system, _log);

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
