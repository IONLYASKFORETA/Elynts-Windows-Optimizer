using System.Collections.ObjectModel;
using System.IO;
using Elynt.Core.Ipc;
using Elynt.Core.Profiles;
using Elynt.Core.Tuning.Cpu;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>
/// The per-game profile editor: pick a game, tick instant changes (power plan, Game Mode and the like), background
/// apps to close, the game's priority, the timer resolution and GPU/CPU tuning, then save. The service applies it when
/// the game starts. Anti-cheat warnings for the game show while editing.
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
    private IReadOnlyList<GpuProfileTuning> _gpuTuning = [];
    private CpuProfileTuning? _cpuTuning;
    private string? _warnings;

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

    /// <summary>GPU settings while the game runs, taken from the Overclocking page.</summary>
    public string GpuTuningSummary => _gpuTuning.Count == 0
        ? "None: the GPU stays as it is."
        : string.Join("; ", _gpuTuning.Select(g => $"{g.DeviceId}: {string.Join(", ", g.Values.Select(v => $"{v.Key} {v.Value}"))}"));

    /// <summary>The CPU tune while the game runs, taken from the Overclocking page.</summary>
    public string CpuTuningSummary => _cpuTuning?.Tune is not { } tune
        ? "None: the CPU stays as it is."
        : string.Join(", ", new (string Name, int? Value, string Unit)[]
            {
                ("Curve Optimizer", tune.CurveOptimizer, ""), ("PPT", tune.PptWatts, " W"), ("TDC", tune.TdcAmps, " A"),
                ("EDC", tune.EdcAmps, " A"), ("PL1", tune.Pl1Watts, " W"), ("PL2", tune.Pl2Watts, " W"),
            }.Where(p => p.Value is not null).Select(p => $"{p.Name} {p.Value}{p.Unit}"));

    /// <summary>Anti-cheat warnings for this game and the profile as edited so far. They warn; saving still works.</summary>
    public string? Warnings
    {
        get => _warnings;
        private set => Set(ref _warnings, value);
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

    /// <summary>Takes the settings of every GPU that isn't on factory settings, as the Overclocking page set them.</summary>
    public async Task UseCurrentGpuAsync()
    {
        if (client() is not { } engine)
            return;
        try
        {
            var report = await engine.GetGpuTuningAsync();
            var tuned = report.Gpus.Where(g => !g.AtDefaults)
                .Select(g => new GpuProfileTuning(g.DeviceId, g.Controls.ToDictionary(c => c.Id, c => c.Current)))
                .ToList();
            if (tuned.Count == 0)
            {
                Message = "Every GPU is on factory settings. Tune it on the Overclocking page first, then take its settings here.";
                return;
            }
            SetGpuTuning(tuned);
            Message = "The GPU settings will be applied while this game runs, once you save.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reading the GPU tuning failed");
            Message = $"Couldn't read the GPU settings: {ex.Message}";
        }
    }

    /// <summary>Takes the CPU tune on now (or the kept one) from the Overclocking page.</summary>
    public async Task UseCurrentCpuAsync()
    {
        if (client() is not { } engine)
            return;
        try
        {
            var report = await engine.GetCpuTuningAsync(advanced: true);
            if ((report.Active ?? report.Kept) is not { } tune)
            {
                Message = "The CPU is on its BIOS settings. Tune it on the Overclocking page first, then take the tune here.";
                return;
            }
            SetCpuTuning(new CpuProfileTuning(tune, Advanced: tune.CurveOptimizer < CpuTuning.CurveOptimizerCap, Unofficial: report.CurveOptimizerUnofficial));
            Message = "The CPU tune will be applied while this game runs, once you save.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reading the CPU tuning failed");
            Message = $"Couldn't read the CPU settings: {ex.Message}";
        }
    }

    public void ClearGpu() => SetGpuTuning([]);

    public void ClearCpu() => SetCpuTuning(null);

    private void SetGpuTuning(IReadOnlyList<GpuProfileTuning> tuning)
    {
        _gpuTuning = tuning;
        OnPropertyChanged(nameof(GpuTuningSummary));
        _ = RefreshWarningsAsync();
    }

    private void SetCpuTuning(CpuProfileTuning? tuning)
    {
        _cpuTuning = tuning;
        OnPropertyChanged(nameof(CpuTuningSummary));
        _ = RefreshWarningsAsync();
    }

    /// <summary>Asks the service for the game's anti-cheat warnings on the profile as edited. Saves nothing.</summary>
    public async Task RefreshWarningsAsync()
    {
        if (client() is not { } engine || Edited() is not { } profile)
        {
            Warnings = null;
            return;
        }
        try
        {
            var check = await engine.CheckProfileAsync(profile);
            Warnings = !check.Succeeded ? check.Message
                : check.Warnings.Count == 0 ? null
                : $"Anti-cheat warnings: {string.Join(" ", check.Warnings)}";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Checking the profile failed");
            Warnings = null;
        }
    }

    public async Task SaveAsync()
    {
        if (client() is not { } engine || Edited() is not { } profile)
            return;
        await RunAsync(() => engine.SaveProfileAsync(profile));
    }

    /// <summary>The profile as the editor shows it now, or null when no game is picked.</summary>
    private GameProfile? Edited() => SelectedGame is not { } game ? null : new GameProfile(game.ExePath)
    {
        TweakIds = Tweaks.Where(t => t.IsChecked).Select(t => t.Id).ToList(),
        AppsToClose = [.. AppsToClose],
        Priority = Priority.Value,
        RaiseTimerResolution = RaiseTimerResolution,
        GpuTuning = _gpuTuning,
        CpuTuning = _cpuTuning,
    };

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
        _gpuTuning = profile?.GpuTuning ?? [];
        _cpuTuning = profile?.CpuTuning;
        OnPropertyChanged(nameof(GpuTuningSummary));
        OnPropertyChanged(nameof(CpuTuningSummary));
        _ = RefreshWarningsAsync();
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
