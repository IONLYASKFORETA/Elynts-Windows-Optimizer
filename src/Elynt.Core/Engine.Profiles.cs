using Elynt.Core.Games;
using Elynt.Core.Profiles;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Logging;

namespace Elynt.Core;

/// <summary>
/// The per-game profile run commands (start, stop, recover after a crash) are on the Engine command surface too, so
/// tests and the service reach them through the same seam as every other command.
/// </summary>
public partial interface IEngine : IProfileEngine;

/// <summary>Per-game profiles: building them, and the changes made while a game runs.</summary>
public sealed partial class Engine
{
    private readonly IProfileStore _profiles = profiles ?? new InMemoryProfileStore();

    public IReadOnlyList<GameProfile> ListProfiles() => _profiles.Load(user);

    public IReadOnlyList<ProfileNote> TakeProfileNotes() => profileNotes?.Take(user) ?? [];

    public ProfileResult SaveProfile(GameProfile profile)
    {
        if (WhyNotInProfile(profile) is { } problem)
            return ProfileResult.Failed($"{problem} The profile wasn't saved.");

        var others = _profiles.Load(user).Where(p => !WinPath.Same(p.ExePath, profile.ExePath));
        _profiles.Save(user, [.. others, profile]);
        return new(true, $"Saved the profile for {GameName(profile.ExePath)}.", profile, AntiCheatClashes(profile));
    }

    public ProfileResult DeleteProfile(string exePath)
    {
        var saved = _profiles.Load(user);
        var kept = saved.Where(p => !WinPath.Same(p.ExePath, exePath)).ToList();
        if (kept.Count == saved.Count)
            return ProfileResult.Failed("There is no profile for that game.");
        _profiles.Save(user, kept);
        return new(true, "Deleted the profile.", null, []);
    }

    /// <summary>The user's profile for this .exe, or null when there is none.</summary>
    public GameProfile? ProfileFor(string exePath) =>
        _profiles.Load(user).FirstOrDefault(p => WinPath.Same(p.ExePath, exePath));

    /// <summary>
    /// The game started: makes the profile's changes, each journaled as temporary before it is made. A change that
    /// fails is reported and the rest still go ahead.
    /// </summary>
    /// <param name="processId">The game's process, for its priority.</param>
    public ProfileRunResult StartProfile(string exePath, int processId)
    {
        if (ProfileFor(exePath) is not { } profile)
            return new(false, $"There is no profile for {WinPath.FileName(exePath)}.", []);

        var problems = new List<string>();
        foreach (var id in profile.TweakIds)
        {
            if (!catalogue.TryGet(id, out var tweak))
            {
                problems.Add($"There is no tweak called '{id}' any more.");
                continue;
            }
            // Checked again here: a profile saved by an older version may hold a tweak that is no longer instant.
            if (WhyNotInProfile(tweak) is { } problem)
            {
                problems.Add(problem);
                continue;
            }
            Note(ApplyTweak(tweak, ApplyOptions.Default, temporary: true), problems);
        }

        foreach (var app in profile.AppsToClose)
            Note(ApplyTweak(CloseApp.Define(app, user), ApplyOptions.Default, temporary: true), problems);

        if (profile.Priority is { } priority)
            Try(() => system.Processes.SetPriority(processId, priority), $"Couldn't set the game's priority to {priority}", problems);

        if (profile.RaiseTimerResolution)
            Try(system.Timer.Raise, "Couldn't raise the timer resolution", problems);

        StartProfileTuning(profile, problems);

        var game = GameName(exePath);
        _log.LogInformation("Started the profile for {Game} for {User}: {Problems} problems", exePath, user, problems.Count);
        return problems.Count == 0
            ? new(true, $"Profile applied for {game}.", [])
            : new(false, $"Profile applied for {game}, except: {string.Join(" ", problems)}", problems);
    }

    /// <summary>The game closed (and its grace period passed): undoes every temporary change and reopens closed apps.</summary>
    public ProfileRunResult StopProfile(string exePath)
    {
        var problems = new List<string>();
        Try(system.Timer.Release, "Couldn't release the timer resolution", problems);
        StopProfileTuning(problems);
        problems.AddRange(UndoTemporaryChanges());

        var game = GameName(exePath);
        _log.LogInformation("Stopped the profile for {Game} for {User}: {Problems} problems", exePath, user, problems.Count);
        return problems.Count == 0
            ? new(true, $"Profile undone for {game}. Your usual settings are back.", [])
            : new(false, $"Profile undone for {game}, except: {string.Join(" ", problems)}", problems);
    }

    /// <summary>
    /// Undoes temporary changes left behind by a crash or reboot mid-game. The service calls this when it starts,
    /// before any game can start a profile.
    /// </summary>
    public ProfileRunResult RecoverTemporaryChanges()
    {
        var open = journal.OpenTemporary(user).Count;
        if (open == 0)
            return new(true, "No per-game profile changes were left behind.", []);

        var problems = UndoTemporaryChanges();
        _log.LogInformation("Recovered {Count} temporary changes for {User}: {Problems} problems", open, user, problems.Count);
        return problems.Count == 0
            ? new(true, $"Put back {open} per-game profile changes left behind when the PC or the service stopped mid-game.", [])
            : new(false, $"Couldn't put back every per-game profile change left behind: {string.Join(" ", problems)}", problems);
    }

    /// <summary>Undoes this user's temporary changes, newest first. Returns what couldn't be undone.</summary>
    private List<string> UndoTemporaryChanges()
    {
        var problems = new List<string>();
        foreach (var entry in journal.OpenTemporary(user).Reverse())
        {
            // A newer change to the same tweak wins: undoing that is the user's to do. (A tweak the user turns on
            // while the profile already has it on takes over the profile's change; see ApplyTweak.)
            if (!IsUndoable(entry))
                continue;
            if (!TryResolve(entry.TweakId, entry, out var tweak))
            {
                problems.Add($"A change by '{entry.TweakId}' couldn't be undone: this version no longer has it.");
                continue;
            }
            var result = Undo(tweak, entry);
            if (!result.Succeeded)
                problems.Add($"{tweak.Name}: {result.Message}");
        }
        return problems;
    }

    /// <summary>Anti-cheat clashes between the profile's tweaks and the game's anti-cheats. They warn, never block.</summary>
    private IReadOnlyList<string> AntiCheatClashes(GameProfile profile)
    {
        try
        {
            if (_games.List().FirstOrDefault(g => g.ExePath is { } known && WinPath.Same(known, profile.ExePath)) is not { } game)
                return [];
            return profile.TweakIds
                .Select(id => catalogue.TryGet(id, out var tweak) ? tweak : null)
                .OfType<TweakDefinition>()
                .SelectMany(t => t.AntiCheat.Where(c => game.AntiCheats.Contains(c.AntiCheat))
                    .Select(c => $"{t.Name}: {AntiCheatWarning.Name(c.AntiCheat)} in {game.Name} ({c.Detail})."))
                .Concat(TuningClashes(profile, game))
                .ToList();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't check the profile against the game's anti-cheat");
            return [];
        }
    }

    private static void Note(CommandResult result, List<string> problems)
    {
        if (!result.Succeeded)
            problems.Add($"{result.Tweak?.Name}: {result.Message}");
    }

    private void Try(Action action, string what, List<string> problems)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "{What}", what);
            problems.Add($"{what}: {ex.Message}");
        }
    }

    private string GameName(string exePath)
    {
        try
        {
            if (_games.List().FirstOrDefault(g => g.ExePath is { } known && WinPath.Same(known, exePath)) is { } game)
                return game.Name;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't list installed games");
        }
        return Path.GetFileNameWithoutExtension(WinPath.FileName(exePath));
    }

    /// <summary>
    /// Why the profile can't be saved, or null when it can. Only instant changes may go in: never a reboot-only
    /// tweak, and never Defender or anything else in Security, whatever the catalogue says.
    /// </summary>
    private string? WhyNotInProfile(GameProfile profile)
    {
        if (!PerGameTweak.IsProgramPath(profile.ExePath))
            return $"'{profile.ExePath}' isn't a full path to a game's .exe.";

        foreach (var id in profile.TweakIds)
        {
            if (!catalogue.TryGet(id, out var tweak))
                return $"There is no tweak called '{id}'.";
            if (WhyNotInProfile(tweak) is { } problem)
                return problem;
        }

        foreach (var app in profile.AppsToClose)
        {
            if (!PerGameTweak.IsProgramPath(app))
                return $"'{app}' isn't a full path to a program.";
            if (WinPath.Same(app, profile.ExePath))
                return "A profile can't close its own game.";
            if (HardRules.Violation(new ProcessTarget(app)) is not null)
                return $"'{WinPath.FileName(app)}' is part of Windows, so a profile never closes it.";
        }

        return WhyNotTuningInProfile(profile);
    }

    private static string? WhyNotInProfile(TweakDefinition tweak)
    {
        if (tweak.Section == Sections.Security)
            return $"'{tweak.Name}' is in Security. Defender and other Security changes are never part of a profile, so security never switches on its own.";
        if (tweak.Risks.HasFlag(RiskFlags.NeedsReboot))
            return $"'{tweak.Name}' needs a reboot, so it can't be switched on and off with a game.";
        if (tweak.PerGame || !tweak.ProfileEligible)
            return $"'{tweak.Name}' doesn't take effect instantly, so it can't be part of a profile.";
        return null;
    }
}
