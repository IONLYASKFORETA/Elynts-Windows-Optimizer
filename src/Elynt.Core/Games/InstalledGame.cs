using Elynt.Core.Tweaks;

namespace Elynt.Core.Games;

/// <summary>Where a game was found.</summary>
public enum GameLauncher
{
    Steam,
    Epic,
    Gog,
    BattleNet,
    Ea,
    Ubisoft,
    Xbox,
    /// <summary>Added by the user by picking the game's .exe.</summary>
    Manual,
}

/// <summary>A game installed on this PC, with the anti-cheats it is known to use.</summary>
/// <param name="InstallPath">The game's folder.</param>
/// <param name="ExePath">The game's .exe, when known (always for a manual add).</param>
public sealed record InstalledGame(
    string Name,
    GameLauncher Launcher,
    string InstallPath,
    string? ExePath,
    IReadOnlyList<AntiCheat> AntiCheats)
{
    public bool Equals(InstalledGame? other) =>
        other is not null && Name == other.Name && Launcher == other.Launcher && InstallPath == other.InstallPath &&
        ExePath == other.ExePath && AntiCheats.SequenceEqual(other.AntiCheats);

    public override int GetHashCode() => HashCode.Combine(Name, Launcher, InstallPath, ExePath);
}

/// <summary>An installed game a tweak would affect, through one of its anti-cheats.</summary>
/// <param name="Detail">The error or rule the anti-cheat shows, from the tweak's conflict.</param>
public sealed record GameConflict(string Game, AntiCheat AntiCheat, AntiCheatSeverity Severity, string Detail);

/// <summary>The outcome of adding or removing a game by hand.</summary>
public sealed record GameCommandResult(bool Succeeded, string Message, InstalledGame? Game);
