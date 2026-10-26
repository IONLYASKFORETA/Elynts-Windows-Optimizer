using Elynt.Core.Apps;
using Elynt.Core.Bios;
using Elynt.Core.Cleanup;
using Elynt.Core.Drivers;
using Elynt.Core.Games;
using Elynt.Core.Journal;
using Elynt.Core.LowLevel;
using Elynt.Core.Repair;
using Elynt.Core.Safety;
using Elynt.Core.Sensors;
using Elynt.Core.Services;
using Elynt.Core.Startup;
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

    /// <summary>What the user must read and confirm before applying, for a tweak undo can still put back.</summary>
    public string? ApplyWarning { get; init; }

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

    /// <summary>
    /// A transient reason the tweak can't be turned on right now, with a guide for clearing it (e.g. Tamper Protection
    /// being on), or null when it can. Unlike <see cref="NotApplicableReason"/>, the user can clear it and try again.
    /// </summary>
    public string? BlockedReason { get; init; }

    public bool Blocked => BlockedReason is not null;

    /// <summary>
    /// On because Windows, the user or another tool set it, with no change of the app's own behind it. It counts as
    /// on, but the app has nothing of its own to undo, so it can't be switched off from the app (ADR 0002).
    /// </summary>
    public bool AlreadySet { get; init; }

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
/// <param name="ConfirmWarning">The user read and confirmed the tweak's <see cref="TweakView.ApplyWarning"/>.</param>
public sealed record ApplyOptions(bool ConfirmDestructive = false, bool Advanced = false, bool ConfirmWarning = false)
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
    bool CanUndo)
{
    /// <summary>
    /// Made by a destructive tweak: there is nothing to put back, so it is never offered for undo and
    /// <see cref="CanUndo"/> is always false (ADR 0002).
    /// </summary>
    public bool Destructive { get; init; }
}

/// <summary>
/// The service's command surface. The pipe mirrors these commands one-to-one, and tests drive the app through
/// them, so this is the single test seam.
/// </summary>
public partial interface IEngine
{
    IReadOnlyList<TweakView> ListTweaks();
    CommandResult Apply(string tweakId, ApplyOptions? options = null);
    CommandResult Revert(string tweakId);

    /// <summary>Every change the app made for this user, newest first.</summary>
    IReadOnlyList<ChangeView> ListHistory();

    /// <summary>
    /// Undoes one change from the history, putting back exactly what was there before it. A destructive change is
    /// refused up front with its warning, before anything is touched.
    /// </summary>
    CommandResult UndoChange(Guid changeId);

    /// <summary>
    /// Undoes every change still in place, newest first. A change whose undo fails is reported and skipped; the rest
    /// still go ahead. Destructive changes are never tried: they are left as they are and named in the message.
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

    /// <summary>
    /// Uninstalls the named installed apps silently, one at a time. Only with <paramref name="confirmed"/>, which the
    /// UI sets once the user confirmed the removal. Edge and WebView2 are refused in every case.
    /// </summary>
    InstallReport UninstallApps(IReadOnlyList<string> appIds, bool confirmed, IProgress<InstallProgress>? progress = null);

    /// <summary>The Repair section's tools, each saying up front whether it needs a restart.</summary>
    IReadOnlyList<RepairToolView> ListRepairTools();

    /// <summary>Runs one repair tool, streaming progress, and saves a log of the run.</summary>
    /// <param name="confirmed">The user confirmed an opt-in tool's warning. Opt-in tools refuse to run without it.</param>
    Task<RepairResult> RunRepairAsync(string toolId, bool confirmed, IProgress<RepairProgress>? progress = null, CancellationToken cancellation = default);

    BiosReport GetBiosReport();

    /// <summary>Every installed service and driver, with who may change it and any recommendation for this PC.</summary>
    IReadOnlyList<ServiceView> ListServices();

    /// <summary>
    /// Sets a service's start type. Reviewed services take Automatic or Manual; Disabled and unreviewed services need
    /// Advanced; protected services and boot drivers are refused in every mode. The whole configuration is journaled.
    /// </summary>
    ServiceResult SetServiceStart(string serviceName, ServiceStart start, ApplyOptions? options = null);

    /// <summary>Puts back the whole configuration a service had before the app last changed it.</summary>
    ServiceResult UndoServiceChange(string serviceName);

    /// <summary>Every startup item (Run keys, Startup folders, sign-in tasks, Store apps) and third-party scheduled task.</summary>
    IReadOnlyList<StartupItemView> ListStartupItems();

    /// <summary>
    /// Turns a startup item on or off through the switch Task Manager uses (Task Scheduler's for tasks). Journaled,
    /// so it can be undone from the Undo history.
    /// </summary>
    StartupCommandResult SetStartupItemEnabled(string itemId, bool enabled);

    /// <summary>
    /// Deletes a startup item (the Run value, Startup folder file or scheduled task). Destructive: refused unless
    /// <paramref name="confirmDestructive"/> says the user confirmed it separately.
    /// </summary>
    StartupCommandResult DeleteStartupItem(string itemId, bool confirmDestructive);

    /// <summary>
    /// Startup items that were added and are on since the last look, for the tray note. The first look for a user
    /// only takes note of what is there. Each new item is reported once.
    /// </summary>
    IReadOnlyList<StartupItemView> CheckNewStartupItems();
    /// <summary>
    /// Measures every cleanup category without deleting anything. Safe categories come back ticked; shader caches
    /// and destructive ones don't. Long-running: streams which category it is on.
    /// </summary>
    Task<CleanupScan> ScanCleanupAsync(IProgress<CleanupProgress>? progress = null, CancellationToken cancellation = default);

    /// <summary>Cleans the ticked categories, streaming progress.</summary>
    /// <param name="confirmDestructive">The user confirmed, in a separate step, that the destructive categories can't be undone.</param>
    Task<CleanupReport> CleanAsync(IReadOnlyList<string> categoryIds, bool confirmDestructive, IProgress<CleanupProgress>? progress = null, CancellationToken cancellation = default);

    CleanupSchedule GetCleanupSchedule();

    /// <summary>Turns weekly cleaning of safe categories on or off. Off by default.</summary>
    CleanupSchedule SetWeeklyCleanup(bool enabled);

    /// <summary>
    /// The service calls this regularly. When this user turned weekly cleaning on and a week has passed, scans and
    /// cleans the safe categories only; otherwise does nothing and returns null.
    /// </summary>
    Task<CleanupReport?> RunWeeklyCleanupIfDueAsync(CancellationToken cancellation = default);

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

    /// <summary>Backups of cleaned drivers, installed drivers known to stop anti-cheats, and DDU's label.</summary>
    DriverCleaningReport GetDriverCleaning();

    /// <summary>
    /// Removes a vendor's display drivers. pnputil exports every package first, so <see cref="RestoreDrivers"/> can
    /// put them back; DDU (downloaded from Wagnardsoft only now) also exports first, and runs only when confirmed.
    /// </summary>
    DriverCleanResult CleanDrivers(DriverCleanRequest request);

    /// <summary>Reinstalls the drivers a clean exported.</summary>
    DriverCleanResult RestoreDrivers(string backupId);

    /// <summary>Installed games from every supported launcher, plus the ones the user added by hand.</summary>
    IReadOnlyList<InstalledGame> ListGames();

    /// <summary>Adds a game the launchers don't know about, by its .exe. Kept for this user.</summary>
    GameCommandResult AddGame(string exePath);

    /// <summary>Removes a game the user added by hand.</summary>
    GameCommandResult RemoveGame(string exePath);

    /// <summary>This user's per-game profiles.</summary>
    IReadOnlyList<Profiles.GameProfile> ListProfiles();

    /// <summary>
    /// Saves (or replaces) the profile for a game. Only instant changes may go in: reboot-only tweaks and Defender or
    /// other Security tweaks are refused. Anti-cheat clashes with the game come back as warnings.
    /// </summary>
    Profiles.ProfileResult SaveProfile(Profiles.GameProfile profile);

    Profiles.ProfileResult DeleteProfile(string exePath);

    /// <summary>Tray notes about profiles applied, undone or skipped since the last call. Each note is returned once.</summary>
    IReadOnlyList<Profiles.ProfileNote> TakeProfileNotes();

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

    /// <summary>The user accepted the GPU vendor's warning (Intel's overclock waiver), which unlocks its tuning.</summary>
    TuningResult AcceptGpuWarning(string deviceId);

    /// <summary>Whether the optional PawnIO driver is installed, with the FACEIT warning naming installed FACEIT games.</summary>
    PawnIoReport GetPawnIo();

    /// <summary>
    /// Downloads, signature-checks and silently installs PawnIO. The only way it is ever installed, and only with
    /// <paramref name="acceptFaceitWarning"/>, which the UI sets once the user has accepted the FACEIT warning.
    /// </summary>
    PawnIoResult InstallPawnIo(bool acceptFaceitWarning);

    /// <summary>"Uninstall driver": removes PawnIO and its modules.</summary>
    PawnIoResult UninstallPawnIo();

    /// <summary>Live temperatures, clocks and loads: from PawnIO when installed, otherwise from vendor SDKs and Windows.</summary>
    SensorReport GetSensors();
}

/// <summary>What happened to one change during restore everything.</summary>
public sealed record UndoOutcome(Guid ChangeId, string TweakName, bool Succeeded, string Message);

public sealed record RestoreResult(bool Succeeded, string Message, IReadOnlyList<UndoOutcome> Changes);

/// <summary>Runs commands for one user, composing the catalogue, the journal and the system adapters.</summary>
public sealed partial class Engine(
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
    ManualUpdate? windowsUpdate = null,
    CleanupCatalogue? cleanup = null,
    ICleanupScheduleStore? cleanupSchedules = null,
    Drift.IDriftStore? driftStore = null,
    Profiles.IProfileStore? profiles = null,
    Profiles.ProfileNotes? profileNotes = null,
    Tuning.Cpu.CpuTuning? cpuTuning = null) : IEngine
{
    private readonly TimeProvider _clock = clock ?? system.Clock;
    private readonly ILogger _log = logger ?? NullLogger<Engine>.Instance;
    private readonly AppInstaller _apps = new(apps ?? AppCatalogue.Default, system.Packages);
    private readonly AppManager _appManager = new(apps ?? AppCatalogue.Default, system.Packages);

    // Built on first use, so they share the Engine's own clock, log and change flow.
    private StartupManager Startup => field ??= new(
        system, journal, user, safetyNet, _clock, _log, (tweak, wording) => ApplyTweak(tweak, ApplyOptions.Default, wording: wording));

    private RepairBench Repair => field ??= new(
        repairs ?? RepairCatalogue.Default, system, repairLogs ?? new InMemoryRepairLogStore(), user, _clock, _log);

    private ServiceDesk Services => field ??= new(system, journal, user);

    public IReadOnlyList<ServiceView> ListServices() => Services.List();

    public ServiceResult SetServiceStart(string serviceName, ServiceStart start, ApplyOptions? options = null) =>
        Services.Set(serviceName, start, options ?? ApplyOptions.Default, (tweak, o, wording) => ApplyTweak(tweak, o, wording: wording));

    public ServiceResult UndoServiceChange(string serviceName) =>
        Services.Undo(serviceName, (id, wording) => Revert(id, wording));

    /// <summary>A catalogue tweak, or the change behind a journaled service start type.</summary>
    private bool TryResolve(string tweakId, JournalEntry? entry, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TweakDefinition? tweak)
    {
        if (catalogue.TryGet(tweakId, out tweak))
            return true;
        tweak = ServiceChange.Resolve(tweakId, entry, system) ?? Profiles.CloseApp.Resolve(tweakId, entry);
        return tweak is not null;
    }

    private CleanupBench Cleanup => field ??= new(
        cleanup ?? CleanupCatalogue.Default, system, cleanupSchedules ?? new InMemoryCleanupScheduleStore(), user, _clock, _log, safetyNet);
    private readonly GameLibrary _games = new(system.Games ?? NoGameSources.Instance, gameStore ?? new InMemoryGameStore(), user);

    public IReadOnlyList<AppView> ListApps() => _apps.ListApps();

    public InstallReport InstallApps(IReadOnlyList<string> appIds, bool allRuntimes, IProgress<InstallProgress>? progress = null, bool allowNotReviewed = false) =>
        _apps.Install(appIds, allRuntimes, progress, allowNotReviewed);

    public AppSearchReport SearchApps(string query) => _appManager.Search(query);

    public InstalledAppsReport ListInstalledApps() => _appManager.ListInstalled();

    public InstallReport UpdateApps(IReadOnlyList<string> appIds, IProgress<InstallProgress>? progress = null) =>
        _appManager.Update(appIds, progress);

    public InstallReport UpdateAllApps(IProgress<InstallProgress>? progress = null) => _appManager.UpdateAll(progress);

    public InstallReport UninstallApps(IReadOnlyList<string> appIds, bool confirmed, IProgress<InstallProgress>? progress = null) =>
        _appManager.Uninstall(appIds, confirmed, progress);

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
        if (!catalogue.TryGet(tweakId, out var tweak))
            return Unknown(tweakId);

        if (tweak.PerGame)
            return Failed(tweak, "Choose a game first: this is set for one game at a time. Nothing was changed.");

        return ApplyTweak(tweak, options ?? ApplyOptions.Default);
    }

    /// <summary>Applies a catalogue tweak, or a change built on the fly (a service start type), through every safeguard.</summary>
    /// <param name="temporary">Made by a per-game profile, so undone when the game closes or after a crash or reboot.</param>
    /// <param name="wording">How the result reads on the page that asked (a startup item, a service); a tweak's by default.</param>
    private CommandResult ApplyTweak(TweakDefinition tweak, ApplyOptions options, bool temporary = false, ChangeWording? wording = null)
    {
        wording ??= ChangeWording.Tweak;
        if (Refusal(tweak, options) is { } refused)
            return refused;

        try
        {
            if (Detect(tweak) == TweakState.On)
            {
                // On only because a running game's profile turned it on: the user wants it kept, so the profile's
                // change becomes theirs. Undo still goes back to how it was before the game.
                if (!temporary && journal.LatestUndoable(tweak.Id, user) is { Temporary: true } profileChange)
                {
                    journal.KeepPermanently(profileChange);
                    _log.LogInformation("Kept {Tweak} for {User} after the game closes", tweak.Id, user);
                    return Done(tweak, $"{wording.Done} A game's profile had it on already; it now stays on after the game closes.");
                }
                return Done(tweak, wording.AlreadyDone);
            }

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
            var turnedOn = note is null ? wording.Done : $"{wording.Done} {note}";

            // Write-ahead: the exact prior state is on disk before anything changes.
            List<StepRecord> records;
            JournalEntry entry;
            try
            {
                records = tweak.Steps.Select(step => step.Capture(system)).ToList();
                entry = journal.Open(tweak.Id, user, system.System.OsBuild, _clock.GetUtcNow(), records, temporary);
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
                return Failed(tweak, $"{wording.CouldNot}: {ex.Message} Nothing was changed.");
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
            return Failed(tweak, $"{wording.CouldNot}: {ex.Message} The prior state is saved; {wording.UndoHint}.");
        }
    }

    /// <summary>Why the Engine won't apply this tweak with these options, or null when it may.</summary>
    private CommandResult? Refusal(TweakDefinition tweak, ApplyOptions options)
    {
        if (WhyNotApplicable(tweak) is { } whyNot)
            return Failed(tweak, $"This doesn't apply to this PC. {whyNot} Nothing was changed.");

        // Hard rules hold in every mode, Advanced included.
        if (tweak.Steps.Select(step => HardRules.Violation(step.Target, system.Services)).FirstOrDefault(v => v is not null) is { } violation)
        {
            _log.LogWarning("Refused {Tweak}: {Violation}", tweak.Id, violation);
            return Failed(tweak, $"This change is never allowed: {violation}. Nothing was changed.");
        }

        if (tweak.Reversibility == Reversibility.Destructive && !options.ConfirmDestructive)
            return Failed(tweak, $"This can't be undone, so it needs a separate confirmation first. {tweak.DestructiveWarning} Nothing was changed.");

        if (tweak.ApplyWarning is { } mustRead && !options.ConfirmWarning)
            return Failed(tweak, $"Read this first and confirm it: {mustRead} Nothing was changed.");

        // A transient, on-the-PC block the user clears by hand (e.g. Defender's Tamper Protection), with a guide.
        if (tweak.Guard?.Blocking(system) is { } blocked)
        {
            _log.LogInformation("Refused {Tweak}: blocked until the user clears it", tweak.Id);
            return Failed(tweak, $"{blocked} Nothing was changed.");
        }

        return null;
    }

    public CommandResult Revert(string tweakId) => Revert(tweakId, ChangeWording.Tweak);

    private CommandResult Revert(string tweakId, ChangeWording wording)
    {
        var entry = journal.LatestUndoable(tweakId, user);
        if (!TryResolve(tweakId, entry, out var tweak))
            return entry is null && wording != ChangeWording.Tweak ? new(false, wording.NothingToUndo, null) : Unknown(tweakId);

        if (entry is null)
            return Failed(tweak, wording.NothingToUndo);

        return Undo(tweak, entry, wording);
    }

    public CommandResult UndoChange(Guid changeId)
    {
        var entry = journal.EntriesFor(user).FirstOrDefault(e => e.Id == changeId);
        if (entry is null)
            return new(false, "There is no such change in your undo history.", null);
        if (StartupManager.Owns(entry.TweakId))
            return UndoStartupChange(entry);
        if (!TryResolve(entry.TweakId, entry, out var tweak))
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
        // Never tried: a destructive change has nothing to put back (ADR 0002), so it is left out and named instead.
        var left = open.Where(IsDestructive).Select(NameOf).ToList();

        var results = new List<UndoOutcome>();
        foreach (var entry in open.Where(entry => !IsDestructive(entry)))
        {
            var name = NameOf(entry);
            var result = UndoChange(entry.Id);
            results.Add(new UndoOutcome(entry.Id, name, result.Succeeded, result.Message));
        }

        var leftAlone = left.Count switch
        {
            0 => "",
            1 => $" 1 change can't be undone, so it was left as it is: {left[0]}.",
            _ => $" {left.Count} changes can't be undone, so they were left as they are: {string.Join(", ", left)}.",
        };
        if (results.Count == 0)
            return new RestoreResult(true, left.Count == 0
                ? "Nothing to undo: every change the app made has already been put back."
                : $"Nothing to undo.{leftAlone}", results);

        var failed = results.Count(r => !r.Succeeded);
        _log.LogInformation("Restore everything for {User}: {Undone} undone, {Failed} not, {Left} left because they can't be undone",
            user, results.Count - failed, failed, left.Count);
        var undoable = results.Count == 1 ? "1 change" : $"{results.Count} changes";
        return failed == 0
            ? new RestoreResult(true, left.Count == 0
                ? $"Put back all {results.Count} changes. Your previous settings are back."
                : $"Put back the {undoable} that can be undone.{leftAlone}", results)
            : new RestoreResult(false, $"Put back {results.Count - failed} of {results.Count} changes. {failed} couldn't be undone; see each one below.{leftAlone}", results);
    }

    private CommandResult Undo(TweakDefinition tweak, JournalEntry entry, ChangeWording? wording = null)
    {
        wording ??= ChangeWording.Tweak;
        // Refused before anything is read or written: a destructive change has nothing to put back (ADR 0002).
        if (IsDestructive(tweak))
            return Failed(tweak, $"This can't be undone. {tweak.DestructiveWarning} Nothing was changed.");
        try
        {
            var conflicts = entry.Steps.Select(step => step.FindConflict(system)).OfType<string>().ToList();
            if (conflicts.Count > 0)
            {
                _log.LogWarning("Not reverting {Tweak} for {User}: {Conflicts}", tweak.Id, user, conflicts);
                return Failed(tweak,
                    $"Didn't undo this. {string.Join(" ", conflicts)} Undoing now would overwrite that, so nothing was changed. " +
                    $"{wording.ChangeAgain} to take a fresh copy of the current settings, or leave it as it is.");
            }

            RevertSteps(entry.Steps);
            journal.CloseReverted(entry, _clock.GetUtcNow());
            _log.LogInformation("Reverted {Tweak} for {User}", tweak.Id, user);
            return Done(tweak, wording.Undone);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Reverting {Tweak} failed", tweak.Id);
            return Failed(tweak, $"Couldn't undo this: {ex.Message}");
        }
    }

    private DriverFlow DriverFlow => field ??= new(system, DriverCleaner, _log);

    public IReadOnlyList<ChangeView> ListHistory()
    {
        var entries = journal.EntriesFor(user);
        return entries
            .Select(entry => new ChangeView(
                entry.Id,
                entry.TweakId,
                NameOf(entry),
                entry.AppliedAt,
                entry.Status switch
                {
                    EntryStatus.Applying => ChangeState.Interrupted,
                    EntryStatus.Applied => ChangeState.Applied,
                    EntryStatus.RolledBack => ChangeState.Failed,
                    _ => ChangeState.Undone,
                },
                entry.Status is EntryStatus.Reverted or EntryStatus.Superseded ? entry.ClosedAt : null,
                IsUndoable(entry) && !IsDestructive(entry))
            {
                Destructive = IsDestructive(entry),
            })
            .Reverse()
            .ToList();
    }

    /// <summary>Only the newest open change to a tweak can be undone: older ones are what it was applied over.</summary>
    private bool IsUndoable(JournalEntry entry) => journal.LatestUndoable(entry.TweakId, entry.User) == entry;

    /// <summary>
    /// Made by a destructive tweak, so there is nothing to put back and it is never offered for undo (ADR 0002). Only
    /// catalogue tweaks are ever destructive; startup switches, service start types and closed apps never are.
    /// </summary>
    private bool IsDestructive(JournalEntry entry) => catalogue.TryGet(entry.TweakId, out var tweak) && IsDestructive(tweak);

    private static bool IsDestructive(TweakDefinition tweak) => tweak.Reversibility == Reversibility.Destructive;

    public IReadOnlyList<RepairToolView> ListRepairTools() => Repair.List();

    public Task<RepairResult> RunRepairAsync(string toolId, bool confirmed, IProgress<RepairProgress>? progress = null, CancellationToken cancellation = default) =>
        Repair.RunAsync(toolId, confirmed, progress, cancellation);

    public BiosReport GetBiosReport() => BiosAdvisor.Build(PawnIo.Fill(TryReadHardware()));

    /// <summary>The board, CPU and memory as firmware reports them, or null when they can't be read. Never throws.</summary>
    private HardwareSnapshot? TryReadHardware()
    {
        try
        {
            return system.Hardware?.Read();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't read the board, CPU and memory details");
            return null;
        }
    }

    public Task<CleanupScan> ScanCleanupAsync(IProgress<CleanupProgress>? progress = null, CancellationToken cancellation = default) =>
        Cleanup.ScanAsync(progress, cancellation);

    public Task<CleanupReport> CleanAsync(IReadOnlyList<string> categoryIds, bool confirmDestructive, IProgress<CleanupProgress>? progress = null, CancellationToken cancellation = default) =>
        Cleanup.CleanAsync(categoryIds, confirmDestructive, progress, cancellation);

    public CleanupSchedule GetCleanupSchedule() => Cleanup.GetSchedule();

    public CleanupSchedule SetWeeklyCleanup(bool enabled) => Cleanup.SetWeekly(enabled);

    /// <summary>
    /// The service calls this regularly. When this user turned weekly cleaning on and a week has passed, cleans the
    /// safe categories only; otherwise does nothing and returns null.
    /// </summary>
    public Task<CleanupReport?> RunWeeklyCleanupIfDueAsync(CancellationToken cancellation = default) =>
        Cleanup.RunWeeklyIfDueAsync(cancellation);
    public UpdateRunStatus StartWindowsUpdate() =>
        windowsUpdate?.Start() ?? new UpdateRunStatus(UpdatePhase.Failed, "Windows Update isn't available from the service.");

    public UpdateRunStatus GetWindowsUpdateStatus() => windowsUpdate?.Status ?? UpdateRunStatus.Idle;

    public DriverReport GetDrivers() => DriverFlow.Report();

    public DriverCheck CheckDriver(DriverRequest request) => DriverFlow.Check(request);

    public DriverInstallResult InstallDriver(DriverRequest request, IProgress<DriverInstallProgress>? progress = null) =>
        DriverFlow.Install(request, progress);


    public DriverCleaningReport GetDriverCleaning() => DriverCleaner.Report();

    public DriverCleanResult CleanDrivers(DriverCleanRequest request) => DriverCleaner.Clean(request);

    public DriverCleanResult RestoreDrivers(string backupId) => DriverCleaner.Restore(backupId);

    private DriverCleaner DriverCleaner => field ??= new(system.DriverCleaning, _games.List, _log);

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

    public TuningResult AcceptGpuWarning(string deviceId) => gpuTuning?.AcceptWarning(deviceId) ?? NoGpuTuning;

    public PawnIoReport GetPawnIo() => PawnIo.Report();

    public PawnIoResult InstallPawnIo(bool acceptFaceitWarning) => PawnIo.Install(acceptFaceitWarning);

    public PawnIoResult UninstallPawnIo() => PawnIo.Uninstall();

    public SensorReport GetSensors() => PawnIo.Sensors();

    private PawnIoManager PawnIo => field ??= new(system, _games.List, _log);

    private static TuningResult NoGpuTuning => new(false, "GPU tuning isn't available on this PC.", null);

    private void RevertSteps(IReadOnlyList<StepRecord> records)
    {
        for (var i = records.Count - 1; i >= 0; i--)
            records[i].Revert(system);
    }

    private string? WhyNotApplicable(TweakDefinition tweak) =>
        UnsupportedReason(tweak.Id) ??
        tweak.Applicability.WhyNot(system.System) ??
        tweak.Steps.Select(step => step.WhyNotHere(system)).FirstOrDefault(reason => reason is not null);

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
        var state = SafeDetect(tweak);
        var change = journal.LatestUndoable(tweak.Id, user);
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
        state,
        change is not null && !IsDestructive(tweak))
    {
        // Read from the journal, not from CanUndo: what matters is whether the app made a change of its own here.
        AlreadySet = state == TweakState.On && change is null,
        Reversibility = tweak.Reversibility,
        DestructiveWarning = tweak.DestructiveWarning,
        ApplyWarning = tweak.ApplyWarning,
        Risks = tweak.Risks,
        AntiCheat = tweak.AntiCheat,
        AffectedGames = affected,
        AntiCheatWarning = Games.AntiCheatWarning.For(affected),
        PendingReboot = tweak.Risks.HasFlag(RiskFlags.NeedsReboot) &&
                        journal.LastChangedAt(tweak.Id, user) > system.System.BootTime,
        NotApplicableReason = WhyNotApplicable(tweak),
        BlockedReason = Blocking(tweak),
        Warnings = tweak.HardwareNotes
            .Where(note => (system.System.Hardware & note.When) == note.When)
            .Select(note => note.Text)
            .ToList(),
        ProfileEligible = tweak.ProfileEligible,
        PerGame = tweak.PerGame,
    };
    }

    /// <summary>A transient block on a tweak (e.g. Tamper Protection on), read safely so a UI list never throws.</summary>
    private string? Blocking(TweakDefinition tweak)
    {
        try
        {
            return tweak.Guard?.Blocking(system);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't read whether {Tweak} is blocked", tweak.Id);
            return null;
        }
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
