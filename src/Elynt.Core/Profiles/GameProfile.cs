using Elynt.Core.Games;

namespace Elynt.Core.Profiles;

/// <summary>How much CPU time Windows gives the game while it runs. Realtime is never offered: it can starve Windows itself.</summary>
public enum GamePriority
{
    Normal,
    AboveNormal,
    High,
}

/// <summary>
/// A per-game profile: instant changes made when the game's .exe starts and undone 15 seconds after it closes.
/// New kinds of change are added as further init properties, so saved profiles keep loading.
/// </summary>
/// <param name="ExePath">The game's .exe, matched against starting processes by full path.</param>
public sealed record GameProfile(string ExePath)
{
    /// <summary>Profile-eligible catalogue tweaks (power plan, Game Mode and the like), applied as temporary changes.</summary>
    public IReadOnlyList<string> TweakIds { get; init; } = [];

    /// <summary>Background programs, by .exe, closed when the game starts and reopened after it closes.</summary>
    public IReadOnlyList<string> AppsToClose { get; init; } = [];

    /// <summary>The game process's priority, or null to leave it as Windows sets it.</summary>
    public GamePriority? Priority { get; init; }

    /// <summary>Asks Windows for a 0.5 ms timer while the game runs.</summary>
    public bool RaiseTimerResolution { get; init; }

    /// <summary>GPU settings while the game runs, one entry per GPU. Applied without the confirm window, since nobody is watching.</summary>
    public IReadOnlyList<GpuProfileTuning> GpuTuning { get; init; } = [];

    /// <summary>A CPU tune while the game runs, or null to leave the CPU as it is.</summary>
    public CpuProfileTuning? CpuTuning { get; init; }

    public bool Equals(GameProfile? other) =>
        other is not null && WinPath.Same(ExePath, other.ExePath) && TweakIds.SequenceEqual(other.TweakIds) &&
        AppsToClose.SequenceEqual(other.AppsToClose) && Priority == other.Priority && RaiseTimerResolution == other.RaiseTimerResolution &&
        GpuTuning.SequenceEqual(other.GpuTuning) && CpuTuning == other.CpuTuning;

    public override int GetHashCode() => HashCode.Combine(ExePath.ToUpperInvariant(), Priority, RaiseTimerResolution);
}

/// <summary>The outcome of saving or deleting a profile.</summary>
/// <param name="Warnings">Anti-cheat clashes between the profile's changes and the game. They warn and never block.</param>
public sealed record ProfileResult(bool Succeeded, string Message, GameProfile? Profile, IReadOnlyList<string> Warnings)
{
    public static ProfileResult Failed(string message) => new(false, message, null, []);
}

/// <summary>What happened when a profile started or stopped, or when changes left behind were recovered.</summary>
/// <param name="Problems">Each change that couldn't be made or undone; the rest still went ahead.</param>
public sealed record ProfileRunResult(bool Succeeded, string Message, IReadOnlyList<string> Problems);
