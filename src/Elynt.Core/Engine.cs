using Elynt.Core.Apps;
using Elynt.Core.Bios;
using Elynt.Core.Drivers;
using Elynt.Core.Games;
using Elynt.Core.Journal;
using Elynt.Core.Repair;
using Elynt.Core.Safety;
using Elynt.Core.Tuning;
using Elynt.Core.Tweaks;
using Elynt.Core.UpdateControl;
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

    /// <summary>Installed games the tweak's anti-cheat conflicts would affect. Shown as a warning; never blocks.</summary>
    public IReadOnlyList<GameConflict> AffectedGames { get; init; } = [];

    /// <summary>A sentence naming the affected installed games, or null when none are installed.</summary>
    public string? AntiCheatWarning { get; init; }

    /// <summary>The tweak needs a reboot and was applied or undone since Windows last started.</summary>
    public bool PendingReboot { get; init; }

    /// <summary>Why the tweak doesn't fit this PC's hardware, edition or build, or null when it does.</summary>
    public string? NotApplicableReason { get; init; }

    public bool Applicable => NotApplicableReason is null;

    /// <summary>Warnings that fit this PC's hardware, e.g. the X3D core-parking warning.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Instant, so a per-game profile may include it.</summary>
    public bool ProfileEligible { get; init; }

    /// <summary>Set for one game at a time: the app asks for the game's .exe and applies the id from <see cref="ForGame"/>.</summary>
    public bool PerGame { get; init; }

    /// <summary>The id of this per-game tweak's instance for one game.</summary>
    public string ForGame(string exePath) => $"{Id}{PerGameTweak.Separator}{exePath}";
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
    /// Installs the ticked apps silently, one at a time, from winget's community source.
    /// <paramref name="allRuntimes"/> adds every Visual C++, DirectX legacy and .NET runtime to the queue.
    /// Apps outside the catalogue install only with <paramref name="allowNotReviewed"/>, which the UI sets once
    /// the user has seen them marked "not reviewed".
    /// </summary>
    InstallReport InstallApps(IReadOnlyList<string> appIds, bool allRuntimes, IProgress<InstallProgress>? progress = null, bool allowNotReviewed = false);

    /// <summary>Searches all of winget's community source. Results outside the catalogue are marked not reviewed.</summary>
    AppSearchReport SearchApps(string query);

    /// <summary>Every app installed on the PC, with the updates waiting in winget's community source.</summary>
    InstalledAppsReport ListInstalledApps();

    /// <summary>Updates the named installed apps, one at a time, from winget's community source.</summary>
    InstallReport UpdateApps(IReadOnlyList<string> appIds, IProgress<InstallProgress>? progress = null);

    /// <summary>Updates every installed app with an update waiting, one at a time.</summary>
    InstallReport UpdateAllApps(IProgress<InstallProgress>? progress = null);

    /// <summary>Uninstalls the named installed apps silently, one at a time.</summary>
    InstallReport UninstallApps(IReadOnlyList<string> appIds, IProgress<InstallProgress>? progress = null);

    /// <summary>The Repair section's tools, each saying up front whether it needs a restart.</summary>
    IReadOnlyList<RepairToolView> ListRepairTools();

    /// <summary>Runs one repair tool, streaming progress, and saves a log of the run.</summary>
    /// <param name="confirmed">The user confirmed an opt-in tool's warning. Opt-in tools refuse to run without it.</param>
    Task<RepairResult> RunRepairAsync(string toolId, bool confirmed, IProgress<RepairProgress>? progress = null, CancellationToken cancellation = default);

    BiosReport GetBiosReport();

    /// <summary>
    /// "Check and install now": starts searching for and installing quality updates in the background, even while
    /// updates are paused, or shows the run already in progress.
    /// </summary>
    UpdateRunStatus StartWindowsUpdate();

    /// <summary>Progress of the latest "check and install now".</summary>
    UpdateRunStatus GetWindowsUpdateStatus();

    /// <summary>The detected GPUs and their installed driver versions.</summary>
    DriverReport GetDrivers();

    /// <summary>Looks up the latest driver for one detected GPU, falling back to the vendor's page.</summary>
    DriverCheck CheckDriver(DriverRequest request);

    /// <summary>
    /// Looks up, downloads, signature-checks and silently installs the latest driver for one detected GPU. A download
    /// that isn't signed by the vendor, or doesn't match the vendor's published hash, is deleted and never run.
    /// </summary>
    /// <param name="progress">Told as each step (lookup, download, signature check, install) starts.</param>
    DriverInstallResult InstallDriver(DriverRequest request, IProgress<DriverInstallProgress>? progress = null);

    /// <summary>Installed games from every supported launcher, plus the ones the user added by hand.</summary>
    IReadOnlyList<InstalledGame> ListGames();

    /// <summary>Adds a game the launchers don't know about, by its .exe. Kept for this user.</summary>
    GameCommandResult AddGame(string exePath);

    /// <summary>Removes a game the user added by hand.</summary>
    GameCommandResult RemoveGame(string exePath);

    /// <summary>Every tunable GPU with its SDK-reported sliders, current values and any change waiting to be kept.</summary>
    GpuTuningReport GetGpuTuning();

    /// <summary>Sets manual GPU values. The change reverts after 15 s unless <see cref="ConfirmGpuTuning"/> keeps it.</summary>
    TuningResult SetGpuTuning(string deviceId, IReadOnlyDictionary<string, int> values);

    /// <summary>Runs the vendor's auto overclock or undervolt, on the same 15 s probation.</summary>
    TuningResult AutoTuneGpu(string deviceId, AutoTuneKind kind);

    TuningResult ConfirmGpuTuning(string deviceId);

    /// <summary>Puts a waiting GPU change back now.</summary>
    TuningResult RevertGpuTuning(string deviceId);

    TuningResult ResetGpuTuning(string deviceId);
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
    AppCatalogue? apps = null,
    IRepairLogStore? repairLogs = null,
    RepairCatalogue? repairs = null,
    IGameStore? gameStore = null,
    GpuTuning? gpuTuning = null,
    ManualUpdate? windowsUpdate = null) : IEngine
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _log = logger ?? NullLogger<Engine>.Instance;
    private readonly AppInstaller _apps = new(apps ?? AppCatalogue.Default, system.Packages);
    private readonly AppManager _appManager = new(apps ?? AppCatalogue.Default, system.Packages);
    private readonly RepairBench _repair = new(
        repairs ?? RepairCatalogue.Default,
        system,
        repairLogs ?? new InMemoryRepairLogStore(),
        user,
        clock ?? TimeProvider.System,
        logger ?? (ILogger)NullLogger<Engine>.Instance);

    private readonly GameLibrary _games = new(system.Games ?? NoGameSources.Instance, gameStore ?? new InMemoryGameStore(), user);

    public IReadOnlyList<AppView> ListApps() => _apps.ListApps();

    public InstallReport InstallApps(IReadOnlyList<string> appIds, bool allRuntimes, IProgress<InstallProgress>? progress = null, bool allowNotReviewed = false) =>
        _apps.Install(appIds, allRuntimes, progress, allowNotReviewed);

    public AppSearchReport SearchApps(string query) => _appManager.Search(query);

    public InstalledAppsReport ListInstalledApps() => _appManager.ListInstalled();

    public InstallReport UpdateApps(IReadOnlyList<string> appIds, IProgress<InstallProgress>? progress = null) =>
        _appManager.Update(appIds, progress);

    public InstallReport UpdateAllApps(IProgress<InstallProgress>? progress = null) => _appManager.UpdateAll(progress);

    public InstallReport UninstallApps(IReadOnlyList<string> appIds, IProgress<InstallProgress>? progress = null) =>
        _appManager.Uninstall(appIds, progress);

    /// <summary>
    /// Every tweak with its detected state, except tweaks that don't fit this PC and ask to be hidden. A hidden tweak
    /// still shows while it has a change to undo.
    /// </summary>
    public IReadOnlyList<TweakView> ListTweaks() => catalogue.Tweaks
        .Concat(GamesWithChanges())
        .Select(Describe)
        .Where(view => view.Applicable || view.CanUndo || !catalogue.Get(view.Id).Applicability.Hide)
        .ToList();

    /// <summary>One game's instance of a per-game tweak is listed while it has a change to undo.</summary>
    private IEnumerable<TweakDefinition> GamesWithChanges() => journal.UndoableTweakIds(user)
        .Where(id => id.Contains(PerGameTweak.Separator))
        .Select(id => catalogue.TryGet(id, out var tweak) ? tweak : null)
        .OfType<TweakDefinition>();

    public CommandResult Apply(string tweakId, ApplyOptions? options = null)
    {
        options ??= ApplyOptions.Default;
        if (!catalogue.TryGet(tweakId, out var tweak))
            return Unknown(tweakId);

        if (tweak.PerGame)
            return Failed(tweak, "Choose a game first: this is set for one game at a time. Nothing was changed.");

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
            var done = Done(tweak, turnedOn);
            // Anti-cheat conflicts warn and never block: the change is made, and the message names the games.
            return done.Tweak?.AntiCheatWarning is { } warning ? done with { Message = $"{turnedOn} {warning}" } : done;
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

    public IReadOnlyList<RepairToolView> ListRepairTools() => _repair.List();

    public Task<RepairResult> RunRepairAsync(string toolId, bool confirmed, IProgress<RepairProgress>? progress = null, CancellationToken cancellation = default) =>
        _repair.RunAsync(toolId, confirmed, progress, cancellation);

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

    public UpdateRunStatus StartWindowsUpdate() =>
        windowsUpdate?.Start() ?? new UpdateRunStatus(UpdatePhase.Failed, "Windows Update isn't available from the service.");

    public UpdateRunStatus GetWindowsUpdateStatus() => windowsUpdate?.Status ?? UpdateRunStatus.Idle;

    public DriverReport GetDrivers() => DriverFlow.Report();

    public DriverCheck CheckDriver(DriverRequest request) => DriverFlow.Check(request);

    public DriverInstallResult InstallDriver(DriverRequest request, IProgress<DriverInstallProgress>? progress = null) =>
        DriverFlow.Install(request, progress);

    private DriverFlow DriverFlow => new(system, _log);

    public IReadOnlyList<InstalledGame> ListGames() => _games.List();

    public GameCommandResult AddGame(string exePath) => _games.Add(exePath);

    public GameCommandResult RemoveGame(string exePath) => _games.Remove(exePath);

    public GpuTuningReport GetGpuTuning() =>
        gpuTuning?.Describe() ?? new GpuTuningReport([], "No GPU on this PC can be tuned from Windows.");

    public TuningResult SetGpuTuning(string deviceId, IReadOnlyDictionary<string, int> values) =>
        gpuTuning?.Set(deviceId, values) ?? NoGpuTuning;

    public TuningResult AutoTuneGpu(string deviceId, AutoTuneKind kind) =>
        gpuTuning?.AutoTune(deviceId, kind) ?? NoGpuTuning;

    public TuningResult ConfirmGpuTuning(string deviceId) => gpuTuning?.Confirm(deviceId) ?? NoGpuTuning;

    public TuningResult RevertGpuTuning(string deviceId) => gpuTuning?.Revert(deviceId) ?? NoGpuTuning;

    public TuningResult ResetGpuTuning(string deviceId) => gpuTuning?.ResetToDefaults(deviceId) ?? NoGpuTuning;

    private static TuningResult NoGpuTuning => new(false, "GPU tuning isn't available on this PC.", null);

    private void RevertSteps(IReadOnlyList<StepRecord> records)
    {
        for (var i = records.Count - 1; i >= 0; i--)
            records[i].Revert(system);
    }

    /// <summary>For the list: a setting that can't be read right now (e.g. bcdedit failing) shows as off, not as an error.</summary>
    private TweakState SafeDetect(TweakDefinition tweak)
    {
        try
        {
            return Detect(tweak);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't read the state of {Tweak}", tweak.Id);
            return TweakState.Off;
        }
    }

    private TweakState Detect(TweakDefinition tweak) =>
        !tweak.PerGame && tweak.Steps.All(step => step.IsApplied(system)) ? TweakState.On : TweakState.Off;

    private TweakView Describe(TweakDefinition tweak)
    {
        var affected = AffectedGames(tweak);
        return new(
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
        SafeDetect(tweak),
        journal.LatestUndoable(tweak.Id, user) is not null)
    {
        Reversibility = tweak.Reversibility,
        DestructiveWarning = tweak.DestructiveWarning,
        Risks = tweak.Risks,
        AntiCheat = tweak.AntiCheat,
        AffectedGames = affected,
        AntiCheatWarning = Games.AntiCheatWarning.For(affected),
        PendingReboot = tweak.Risks.HasFlag(RiskFlags.NeedsReboot) &&
                        journal.LastChangedAt(tweak.Id, user) > system.System.BootTime,
        NotApplicableReason = tweak.Applicability.WhyNot(system.System),
        Warnings = tweak.HardwareNotes
            .Where(note => (system.System.Hardware & note.When) == note.When)
            .Select(note => note.Text)
            .ToList(),
        ProfileEligible = tweak.ProfileEligible,
        PerGame = tweak.PerGame,
    };
    }

    private IReadOnlyList<GameConflict> AffectedGames(TweakDefinition tweak)
    {
        try
        {
            return _games.Affected(tweak.AntiCheat);
        }
        catch (Exception ex)
        {
            // Game detection only adds names to a warning; it must never stop a tweak from showing or applying.
            _log.LogWarning(ex, "Couldn't list installed games for {Tweak}", tweak.Id);
            return [];
        }
    }

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
