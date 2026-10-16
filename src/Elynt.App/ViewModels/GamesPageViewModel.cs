using System.Collections.ObjectModel;
using Elynt.Core.Games;

namespace Elynt.App.ViewModels;

/// <summary>The installed games the service found, plus the ones the user added by hand.</summary>
public sealed class GamesPageViewModel : ObservableObject
{
    private string? _message;

    public ObservableCollection<GameItem> Games { get; } = [];

    /// <summary>The service's answer to the last add or remove.</summary>
    public string? Message
    {
        get => _message;
        set => Set(ref _message, value);
    }

    public bool IsEmpty => Games.Count == 0;

    public void Show(IReadOnlyList<InstalledGame> games)
    {
        Games.Clear();
        foreach (var game in games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
            Games.Add(new GameItem(game));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public sealed record GameItem(InstalledGame Game)
    {
        public string Name => Game.Name;

        public string Detail => string.Join(" · ", new[] { LauncherName(Game.Launcher), Game.InstallPath }
            .Concat(Game.AntiCheats.Select(a => $"Anti-cheat: {AntiCheatWarning.Name(a)}")));

        /// <summary>Only games added by hand can be removed; launcher games go when the launcher uninstalls them.</summary>
        public bool CanRemove => Game.Launcher == GameLauncher.Manual;

        private static string LauncherName(GameLauncher launcher) => launcher switch
        {
            GameLauncher.Steam => "Steam",
            GameLauncher.Epic => "Epic Games",
            GameLauncher.Gog => "GOG",
            GameLauncher.BattleNet => "Battle.net",
            GameLauncher.Ea => "EA app",
            GameLauncher.Ubisoft => "Ubisoft Connect",
            GameLauncher.Xbox => "Xbox app",
            _ => "Added by you",
        };
    }
}
