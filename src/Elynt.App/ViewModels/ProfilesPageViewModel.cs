using System.Collections.ObjectModel;
using System.IO;
using Elynt.Core.Ipc;
using Elynt.Core.Profiles;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>
/// The per-game profile editor: pick a game, tick instant changes (power plan, Game Mode and the like), background
/// apps to close, the game's priority and the timer resolution, then save. The service applies it when the game starts.
/// </summary>
/// <param name="client">The current connection to the service, or null while disconnected.</param>
public sealed class ProfilesPageViewModel(Func<EnginePipeClient?> client) : ObservableObject
{
    private IReadOnlyList<GameProfile> _saved = [];
    private GameChoice? _selectedGame;
    private PriorityChoice _priority = PriorityChoice.All[0];
    private bool _raiseTimerResolution;
    private string? _message;
    private string? _selectedApp;

    /// <summary>Games with a known .exe; marked when they already have a profile.</summary>
    public ObservableCollection<GameChoice> Games { get; } = [];

    /// <summary>Every profile-eligible tweak, as a tick box.</summary>
    public ObservableCollection<ProfileTweakChoice> Tweaks { get; } = [];

    public ObservableCollection<string> AppsToClose { get; } = [];

    public IReadOnlyList<PriorityChoice> Priorities => PriorityChoice.All;

    /// <summary>Picks a program's .exe for the apps-to-close list. Set by the window.</summary>
    public Func<string?> PickApp { get; set; } = () => null;

    public GameChoice? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (Set(ref _selectedGame, value))
            {
                ShowProfile();
                OnPropertyChanged(nameof(HasGame));
            }
        }
    }

    public bool HasGame => SelectedGame is not null;

    public PriorityChoice Priority
    {
        get => _priority;
        set => Set(ref _priority, value);
    }

    public bool RaiseTimerResolution
    {
        get => _raiseTimerResolution;
        set => Set(ref _raiseTimerResolution, value);
    }

    public string? SelectedApp
    {
        get => _selectedApp;
        set => Set(ref _selectedApp, value);
    }

    /// <summary>The service's answer to the last save or delete, with any anti-cheat warnings.</summary>
    public string? Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    /// <summary>Whether profiles raise quiet tray notes when they apply, undo or are skipped. Saved for this user.</summary>
    public bool NotifyProfiles
    {
        get => TrayNoteSettings.Current.Profiles;
        set
        {
            TrayNoteSettings.Current.Profiles = value;
            TrayNoteSettings.Current.Save();
            OnPropertyChanged();
        }
    }

    public async Task LoadAsync()
    {
        if (client() is not { } engine)
            return;
        try
        {
            var tweaks = await engine.ListTweaksAsync();
            Tweaks.Clear();
            foreach (var tweak in tweaks.Where(t => t.ProfileEligible && !t.PerGame && t.Applicable))
                Tweaks.Add(new ProfileTweakChoice(tweak.Id, tweak.Name, tweak.WhatItDoes));

            _saved = await engine.ListProfilesAsync();
            var games = await engine.ListGamesAsync();
            var selected = SelectedGame?.ExePath;
            Games.Clear();
            foreach (var game in games.Where(g => g.ExePath is not null).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
                Games.Add(new GameChoice(game.Name, game.ExePath!, HasProfile(game.ExePath!)));
            // A profile whose game is no longer listed can still be edited or deleted.
            foreach (var orphan in _saved.Where(p => !Games.Any(g => WinPathSame(g.ExePath, p.ExePath))))
                Games.Add(new GameChoice(Path.GetFileNameWithoutExtension(orphan.ExePath), orphan.ExePath, true));

            _selectedGame = Games.FirstOrDefault(g => selected is not null && WinPathSame(g.ExePath, selected)) ?? Games.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedGame));
            OnPropertyChanged(nameof(HasGame));
            ShowProfile();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading per-game profiles failed");
            Message = $"Couldn't read the profiles: {ex.Message}";
        }
    }

    public void AddApp()
    {
        if (PickApp() is { } exe && !AppsToClose.Contains(exe, StringComparer.OrdinalIgnoreCase))
            AppsToClose.Add(exe);
    }

    public void RemoveApp()
    {
        if (SelectedApp is { } app)
            AppsToClose.Remove(app);
    }

    public async Task SaveAsync()
    {
        if (client() is not { } engine || SelectedGame is not { } game)
            return;
        var profile = new GameProfile(game.ExePath)
        {
            TweakIds = Tweaks.Where(t => t.IsChecked).Select(t => t.Id).ToList(),
            AppsToClose = [.. AppsToClose],
            Priority = Priority.Value,
            RaiseTimerResolution = RaiseTimerResolution,
        };
        await RunAsync(() => engine.SaveProfileAsync(profile));
    }

    public async Task DeleteAsync()
    {
        if (client() is not { } engine || SelectedGame is not { } game)
            return;
        await RunAsync(() => engine.DeleteProfileAsync(game.ExePath));
    }

    private async Task RunAsync(Func<Task<ProfileResult>> command)
    {
        try
        {
            var result = await command();
            Log.Information("Profile command: {Succeeded} {Message}", result.Succeeded, result.Message);
            var message = result.Warnings.Count == 0 ? result.Message : $"{result.Message} Anti-cheat warnings: {string.Join(" ", result.Warnings)}";
            await LoadAsync();
            Message = message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "A profile command failed");
            Message = $"The service couldn't do this: {ex.Message}";
        }
    }

    private void ShowProfile()
    {
        var profile = SelectedGame is { } game ? _saved.FirstOrDefault(p => WinPathSame(p.ExePath, game.ExePath)) : null;
        foreach (var tweak in Tweaks)
            tweak.IsChecked = profile?.TweakIds.Contains(tweak.Id) == true;
        AppsToClose.Clear();
        foreach (var app in profile?.AppsToClose ?? [])
            AppsToClose.Add(app);
        Priority = PriorityChoice.All.First(p => p.Value == profile?.Priority);
        RaiseTimerResolution = profile?.RaiseTimerResolution == true;
        Message = SelectedGame is null ? null : profile is null ? "This game has no profile yet." : null;
    }

    private bool HasProfile(string exePath) => _saved.Any(p => WinPathSame(p.ExePath, exePath));

    private static bool WinPathSame(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public sealed record GameChoice(string Name, string ExePath, bool HasProfile)
    {
        public string Label => HasProfile ? $"{Name} (has a profile)" : Name;
    }

    public sealed record PriorityChoice(string Label, GamePriority? Value)
    {
        public static IReadOnlyList<PriorityChoice> All { get; } =
        [
            new("Leave as Windows sets it", null),
            new("Normal", GamePriority.Normal),
            new("Above normal", GamePriority.AboveNormal),
            new("High", GamePriority.High),
        ];
    }
}

/// <summary>One instant tweak the profile can include.</summary>
public sealed class ProfileTweakChoice(string id, string name, string whatItDoes) : ObservableObject
{
    private bool _isChecked;

    public string Id { get; } = id;
    public string Name { get; } = name;
    public string WhatItDoes { get; } = whatItDoes;

    public bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value);
    }
}
