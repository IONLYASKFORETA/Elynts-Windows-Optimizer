using Elynt.Core.Tweaks;

namespace Elynt.Core.Games;

/// <summary>Every installed game for one user: what the launchers recorded plus the user's own .exe adds.</summary>
internal sealed class GameLibrary(IGameSources pc, IGameStore store, string user)
{
    private IReadOnlyList<InstalledGame>? _found;

    public IReadOnlyList<InstalledGame> List()
    {
        // Launcher scans read many files, so they run once per engine (one app connection); manual adds are re-read.
        _found ??= Scan();
        var manual = store.Load(user)
            .Where(exe => !_found.Any(g => g.ExePath is { } known && WinPath.Same(known, exe)))
            .Select(Manual);
        return [.. _found, .. manual];
    }

    public GameCommandResult Add(string exePath)
    {
        var exe = exePath.Trim().Trim('"');
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return new(false, $"'{WinPath.FileName(exe)}' isn't a program. Pick the game's .exe file.", null);
        if (!pc.FileExists(exe))
            return new(false, $"'{exe}' couldn't be found. Nothing was added.", null);

        exe = WinPath.Join(exe);
        if (List().FirstOrDefault(g => g.ExePath is { } known && WinPath.Same(known, exe)) is { } existing)
            return new(true, $"{existing.Name} is already in your games.", existing);

        store.Save(user, [.. store.Load(user), exe]);
        var game = Manual(exe);
        return new(true, $"Added {game.Name}.", game);
    }

    public GameCommandResult Remove(string exePath)
    {
        var saved = store.Load(user);
        var kept = saved.Where(p => !WinPath.Same(p, exePath.Trim().Trim('"'))).ToList();
        if (kept.Count == saved.Count)
            return new(false, "That game wasn't added by hand, so it can't be removed here.", null);
        store.Save(user, kept);
        return new(true, "Removed.", null);
    }

    private List<InstalledGame> Scan()
    {
        var games = new List<InstalledGame>();
        foreach (var scan in Scans)
        {
            try
            {
                foreach (var found in scan(pc))
                    if (!games.Any(g => WinPath.Same(g.InstallPath, found.InstallPath)))
                        games.Add(Describe(found));
            }
            catch (Exception)
            {
                // One launcher's broken records must not hide every other launcher's games.
            }
        }
        return games;
    }

    private static readonly Func<IGameSources, IEnumerable<FoundGame>>[] Scans =
    [
        Launchers.Steam,
        Launchers.Epic,
        Launchers.Gog,
        Launchers.Ubisoft,
        Launchers.BattleNet,
        Launchers.Ea,
        Launchers.Xbox,
    ];

    private InstalledGame Manual(string exe) =>
        Describe(new FoundGame(Path.GetFileNameWithoutExtension(WinPath.FileName(exe)), GameLauncher.Manual, WinPath.Parent(exe), exe));

    private InstalledGame Describe(FoundGame found) =>
        new(found.Name, found.Launcher, found.InstallPath, found.ExePath, KnownGames.For(found, pc));

    /// <summary>The installed games each of the tweak's anti-cheat conflicts would affect, in the tweak's order.</summary>
    public IReadOnlyList<GameConflict> Affected(IReadOnlyList<AntiCheatConflict> conflicts)
    {
        if (conflicts.Count == 0)
            return [];
        var games = List();
        return conflicts
            .SelectMany(c => games.Where(g => g.AntiCheats.Contains(c.AntiCheat)).Select(g => new GameConflict(g.Name, c.AntiCheat, c.Severity, c.Detail)))
            .ToList();
    }
}
