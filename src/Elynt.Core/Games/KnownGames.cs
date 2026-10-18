using Elynt.Core.Tweaks;

namespace Elynt.Core.Games;

/// <summary>
/// Games whose anti-cheat is named in research/anti-cheat-requirements.md, and how to recognise them: a Steam app id,
/// an Epic AppName, an .exe name, or words in the launcher's display name. Unknown games are still checked for an
/// anti-cheat folder (EasyAntiCheat, BattlEye) in their install folder.
/// </summary>
internal static class KnownGames
{
    private sealed record Known(
        AntiCheat AntiCheat,
        string[] Titles,
        string[]? SteamAppIds = null,
        string[]? EpicAppNames = null,
        string[]? Exes = null);

    // Titles are compared with letters and digits only, lower case, as a substring of the game's name.
    private static readonly Known[] Games =
    [
        new(AntiCheat.Vanguard, ["valorant"], Exes: ["VALORANT.exe", "VALORANT-Win64-Shipping.exe"]),
        new(AntiCheat.Vanguard, ["leagueoflegends"], Exes: ["League of Legends.exe", "LeagueClient.exe"]),
        new(AntiCheat.Vanguard, ["2xko"]),
        new(AntiCheat.FaceIt, ["counterstrike2"], SteamAppIds: ["730"], Exes: ["cs2.exe"]),
        new(AntiCheat.EasyAntiCheat, ["fortnite"], EpicAppNames: ["Fortnite"], Exes: ["FortniteLauncher.exe", "FortniteClient-Win64-Shipping.exe"]),
        new(AntiCheat.EasyAntiCheat, ["apexlegends"], SteamAppIds: ["1172470"], Exes: ["r5apex.exe"]),
        new(AntiCheat.BattlEye, ["rainbowsixsiege"], SteamAppIds: ["359550"], Exes: ["RainbowSix.exe", "RainbowSix_BE.exe"]),
        new(AntiCheat.BattlEye, ["pubg", "playerunknownsbattlegrounds"], SteamAppIds: ["578080"], Exes: ["TslGame.exe"]),
        new(AntiCheat.BattlEye, ["dayz"], SteamAppIds: ["221100"], Exes: ["DayZ_x64.exe"]),
        new(AntiCheat.BattlEye, ["arma3"], SteamAppIds: ["107410"], Exes: ["arma3_x64.exe"]),
        new(AntiCheat.BattlEye, ["escapefromtarkov"], Exes: ["EscapeFromTarkov.exe"]),
        new(AntiCheat.Ricochet, ["callofduty"], SteamAppIds: ["1938090"], Exes: ["cod.exe"]),
        new(AntiCheat.Javelin, ["battlefield6"], SteamAppIds: ["2807960"], Exes: ["bf6.exe"]),
    ];

    /// <summary>Folders an anti-cheat installs inside the game's own folder.</summary>
    private static readonly (string Folder, AntiCheat AntiCheat)[] Folders =
    [
        ("EasyAntiCheat", AntiCheat.EasyAntiCheat),
        ("EasyAntiCheat_EOS", AntiCheat.EasyAntiCheat),
        ("BattlEye", AntiCheat.BattlEye),
    ];

    public static IReadOnlyList<AntiCheat> For(FoundGame game, IGameSources pc)
    {
        var title = Simplify(game.Name);
        var exe = game.ExePath is { } path ? WinPath.FileName(path) : null;
        var found = Games.Where(k =>
                (game.Launcher == GameLauncher.Steam && k.SteamAppIds?.Contains(game.LauncherId) == true) ||
                (game.Launcher == GameLauncher.Epic && k.EpicAppNames?.Contains(game.LauncherId, StringComparer.OrdinalIgnoreCase) == true) ||
                (exe is not null && k.Exes?.Contains(exe, StringComparer.OrdinalIgnoreCase) == true) ||
                k.Titles.Any(t => title.Contains(t, StringComparison.Ordinal)))
            .Select(k => k.AntiCheat);

        var folders = new List<string> { game.InstallPath };
        if (game.ExePath is { } exePath && !WinPath.Same(WinPath.Parent(exePath), game.InstallPath))
            folders.Add(WinPath.Parent(exePath));
        var shipped = Folders
            .Where(f => folders.Any(dir => pc.DirectoryExists(WinPath.Join(dir, f.Folder))))
            .Select(f => f.AntiCheat);

        return found.Concat(shipped).Distinct().ToList();
    }

    private static string Simplify(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
