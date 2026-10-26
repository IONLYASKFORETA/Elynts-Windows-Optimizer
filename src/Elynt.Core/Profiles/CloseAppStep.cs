using Elynt.Core.Games;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Profiles;

/// <summary>
/// Closing one background app while a game runs, run and journaled like a tweak under its own id
/// (<c>profile.close-app|C:\path\app.exe</c>), so undo reopens it, even after a crash or reboot.
/// </summary>
public static class CloseApp
{
    public const string IdPrefix = "profile.close-app|";

    public static string TweakIdFor(string exePath) => IdPrefix + exePath;

    public static TweakDefinition Define(string exePath, string user) => new(
        TweakIdFor(exePath),
        Sections.Gaming,
        $"Close {WinPath.FileName(exePath)} while playing",
        $"Closes {WinPath.FileName(exePath)} when the game starts.",
        "Chosen by you in a per-game profile.",
        [],
        EvidenceTier.Situational,
        "Opens it again after the game closes.",
        [new CloseAppStep(exePath, user)])
    {
        ProfileEligible = true,
    };

    /// <summary>The change behind a journaled app close, or null when the id isn't one.</summary>
    internal static TweakDefinition? Resolve(string tweakId, JournalEntry? entry) =>
        tweakId.StartsWith(IdPrefix, StringComparison.Ordinal)
            ? Define(tweakId[IdPrefix.Length..], entry?.User ?? "")
            : null;
}

/// <summary>Closes every process running from <paramref name="ExePath"/>. "On" means it isn't running.</summary>
public sealed record CloseAppStep(string ExePath, string User) : TweakStep
{
    public override ChangeTarget Target => new ProcessTarget(ExePath);

    public override string DescribeChange() => $"Close {ExePath}";

    public override bool IsApplied(SystemAdapters system) => !system.Processes.IsRunning(ExePath);

    public override StepRecord Capture(SystemAdapters system) => new ClosedAppRecord(ExePath, User);

    public override void Apply(SystemAdapters system) => system.Processes.Close(ExePath);
}

/// <summary>An app the profile closed, reopened for <paramref name="User"/> on undo unless it is already running again.</summary>
public sealed record ClosedAppRecord(string ExePath, string User) : StepRecord
{
    /// <summary>The app was running in one account's session, and undo starts it again there.</summary>
    public override bool AffectsEveryAccount() => false;

    public override string? FindConflict(SystemAdapters system) => null;

    public override void Revert(SystemAdapters system)
    {
        if (!system.Processes.IsRunning(ExePath))
            system.Processes.Start(ExePath, User);
    }
}
