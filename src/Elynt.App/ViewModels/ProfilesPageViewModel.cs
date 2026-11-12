using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Elynt.Core.Games;
using Elynt.Core.Ipc;
using Elynt.Core.Profiles;
using Elynt.Core.Tuning.Cpu;
using Elynt.Core.Tweaks;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>
/// The Per-game profiles page, games first: the games found on this PC, each marked when it has a profile, and the
/// profile editor for the selected game, which the detail pane shows. The editor holds instant changes (the power plan,
/// Game Mode and the like), background apps to close, the game's priority, the timer resolution and GPU/CPU tuning;
/// the service applies them when the game starts. Anti-cheat warnings for the game show while editing.
/// </summary>
/// <param name="client">The current connection to the service, or null while disconnected.</param>
public sealed class ProfilesPageViewModel(Func<EnginePipeClient?> client) : ObservableObject
{
    private IReadOnlyList<GameProfile> _saved = [];
    private string _filter = "";
    private GameRow? _selectedGame;
    private string? _chosenExe;
    private PriorityChoice _priority = PriorityChoice.All[0];
    private bool _raiseTimerResolution;
    private string? _message;
    private string? _gamesMessage;
    private IReadOnlyList<GpuProfileTuning> _gpuTuning = [];
    private CpuProfileTuning? _cpuTuning;
    private string? _warnings;

    /// <summary>
    /// Every game found on this PC, by name, marked when it has a profile; then any profile whose game is no longer
    /// found, so it can still be edited or deleted.
    /// </summary>
    public ObservableCollection<GameRow> Games { get; } = [];

    /// <summary>The games "Find a game" matches, in the same order: the rows the page draws.</summary>
    public ObservableCollection<GameRow> Shown { get; } = [];

    /// <summary>Part of a game's name, launcher or anti-cheat, typed into "Find a game" over the list.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value))
                ShowMatches();
        }
    }

    /// <summary>How many games the list shows, under "Find a game": "4 games", or why there are none.</summary>
    public string ResultCount => Shown.Count switch
    {
        0 when Filter.Trim().Length > 0 => $"No games match \"{Filter.Trim()}\".",
        0 => "No games found yet. Add one by its .exe.",
        1 => "1 game",
        var count => $"{count} games",
    };

    /// <summary>
    /// Profile-eligible tweaks that fight each other (the power plans), each set as one pick-one choice, as on their
    /// own page, so a profile can't hold two.
    /// </summary>
    public ObservableCollection<ProfileChoice> Choices { get; } = [];

    /// <summary>Every other profile-eligible tweak, as a tick box.</summary>
    public ObservableCollection<ProfileTweakChoice> Tweaks { get; } = [];

    /// <summary>Background programs closed when the game starts and opened again after it closes.</summary>
    public ObservableCollection<AppToClose> AppsToClose { get; } = [];

    public IReadOnlyList<PriorityChoice> Priorities => PriorityChoice.All;

    /// <summary>Picks a program's .exe for the apps-to-close list. Set by the window.</summary>
    public Func<string?> PickApp { get; set; } = () => null;

    /// <summary>Picks a game's .exe, opening in the game's folder. Set by the window.</summary>
    public Func<string, string?> PickGameExe { get; set; } = _ => null;

    /// <summary>The game whose profile the detail pane shows.</summary>
    public GameRow? SelectedGame
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

    /// <summary>
    /// The .exe the profile is for: the selected game's, or for a game its launcher gave no .exe for, the one chosen
    /// for it. Null until that is chosen.
    /// </summary>
    public string? ExePath => SelectedGame?.ExePath ?? _chosenExe;

    /// <summary>
    /// The selected game's launcher gave only its folder (Steam, Battle.net), so the editor asks for its .exe before it
    /// can make a profile: profiles start when that .exe does.
    /// </summary>
    public bool NeedsExe => HasGame && ExePath is null;

    /// <summary>The editor's changes show once the profile's .exe is known.</summary>
    public bool CanEdit => ExePath is not null;

    /// <summary>Why the editor asks for the .exe.</summary>
    public string ExeNote => SelectedGame is { } game
        ? $"{game.Launcher} gives only {game.Name}'s folder, not the .exe that starts it. Choose that .exe to make a profile."
        : "";

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

    /// <summary>The service's answer to the last game added or removed, under "Find a game".</summary>
    public string? GamesMessage
    {
        get => _gamesMessage;
        set => Set(ref _gamesMessage, value);
    }

    /// <summary>Reads the games, the profiles and the tweaks a profile can hold, keeping the selected game.</summary>
    /// <param name="select">A game to select instead, such as one just added.</param>
    public async Task LoadAsync(InstalledGame? select = null)
    {
        if (client() is not { } engine)
            return;
        try
        {
            var eligible = (await engine.ListTweaksAsync()).Where(t => t.ProfileEligible && !t.PerGame && t.Applicable).ToList();
            Choices.Clear();
            foreach (var group in eligible.Where(t => t.Choice is not null).GroupBy(t => t.Choice!.Id))
                Choices.Add(new ProfileChoice(group.First().Choice!, group.Select(t => t.Id).ToList()));
            Tweaks.Clear();
            foreach (var tweak in eligible.Where(t => t.Choice is null))
                Tweaks.Add(new ProfileTweakChoice(tweak.Id, tweak.Name, tweak.WhatItDoes));

            _saved = await engine.ListProfilesAsync();
            var games = await engine.ListGamesAsync();
            var selected = SelectedGame;
            Games.Clear();
            foreach (var row in GameRow.All(games, _saved))
                Games.Add(row);
            Shown.Clear();
            ShowMatches();

            _selectedGame = Games.FirstOrDefault(g => select is not null && select.Equals(g.Game))
                ?? Games.FirstOrDefault(g => selected is not null && g.IsSameGame(selected))
                ?? Games.FirstOrDefault();
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

    /// <summary>
    /// Brings <see cref="Shown"/> to the games that match, taking out and putting in rows rather than refilling it, so
    /// the list keeps its selection while the selected game still matches.
    /// </summary>
    private void ShowMatches()
    {
        var search = Filter.Trim();
        var matches = Games.Where(g => Matches(g.Name) || Matches(g.Detail)).ToList();
        for (var i = Shown.Count - 1; i >= 0; i--)
        {
            if (!matches.Contains(Shown[i]))
                Shown.RemoveAt(i);
        }
        for (var i = 0; i < matches.Count; i++)
        {
            if (i >= Shown.Count || Shown[i] != matches[i])
                Shown.Insert(i, matches[i]);
        }
        OnPropertyChanged(nameof(ResultCount));

        bool Matches(string text) => text.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Asks for the .exe of a game its launcher gave no .exe for, from the game's folder. One from elsewhere is refused:
    /// the profile would then be for another program.
    /// </summary>
    public void ChooseExe()
    {
        if (SelectedGame is not { Game: { } game } || PickGameExe(game.InstallPath) is not { } exe)
            return;
        if (!IsInside(exe, game.InstallPath))
        {
            Message = $"{FileName(exe)} isn't in {game.Name}'s folder ({game.InstallPath}). Choose the .exe inside it that starts the game.";
            return;
        }
        _chosenExe = exe;
        ExeChanged();
        Message = "This game has no profile yet.";
        _ = RefreshWarningsAsync();
    }

    private void ExeChanged()
    {
        OnPropertyChanged(nameof(ExePath));
        OnPropertyChanged(nameof(NeedsExe));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(ExeNote));
    }

    public void AddApp()
    {
        if (PickApp() is { } exe && !AppsToClose.Any(a => WinPathSame(a.Path, exe)))
            AppsToClose.Add(new AppToClose(exe));
    }

    /// <summary>Takes one app off the list, from the "×" on its row.</summary>
    public void RemoveApp(AppToClose app) => AppsToClose.Remove(app);

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

    /// <summary>The profile as the editor shows it now, or null when no game with a known .exe is picked.</summary>
    private GameProfile? Edited() => ExePath is not { } exe ? null : new GameProfile(exe)
    {
        TweakIds = [.. Choices.Select(c => c.ChosenTweakId).OfType<string>(), .. Tweaks.Where(t => t.IsChecked).Select(t => t.Id)],
        AppsToClose = [.. AppsToClose.Select(a => a.Path)],
        Priority = Priority.Value,
        RaiseTimerResolution = RaiseTimerResolution,
        GpuTuning = _gpuTuning,
        CpuTuning = _cpuTuning,
    };

    public async Task DeleteAsync()
    {
        if (client() is not { } engine || ExePath is not { } exe)
            return;
        await RunAsync(() => engine.DeleteProfileAsync(exe));
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
        _chosenExe = null;
        ExeChanged();
        var profile = ExePath is { } exe ? _saved.FirstOrDefault(p => WinPathSame(p.ExePath, exe)) : null;
        foreach (var choice in Choices)
            choice.Show(profile?.TweakIds ?? []);
        foreach (var tweak in Tweaks)
            tweak.IsChecked = profile?.TweakIds.Contains(tweak.Id) == true;
        AppsToClose.Clear();
        foreach (var app in profile?.AppsToClose ?? [])
            AppsToClose.Add(new AppToClose(app));
        Priority = PriorityChoice.All.First(p => p.Value == profile?.Priority);
        RaiseTimerResolution = profile?.RaiseTimerResolution == true;
        _gpuTuning = profile?.GpuTuning ?? [];
        _cpuTuning = profile?.CpuTuning;
        OnPropertyChanged(nameof(GpuTuningSummary));
        OnPropertyChanged(nameof(CpuTuningSummary));
        _ = RefreshWarningsAsync();
        // A game that needs its .exe first says so in its own note.
        Message = !CanEdit ? null : profile is null ? "This game has no profile yet." : null;
    }

    /// <summary>Whether two Windows paths name the same file, on any OS the tests run on.</summary>
    private static bool WinPathSame(string a, string b) =>
        string.Equals(a.Replace('/', '\\').TrimEnd('\\'), b.Replace('/', '\\').TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a Windows path is somewhere under a folder, on any OS the tests run on.</summary>
    private static bool IsInside(string path, string folder) =>
        path.Replace('/', '\\').StartsWith(folder.Replace('/', '\\').TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>A Windows path's file name, on any OS the tests run on.</summary>
    private static string FileName(string path) => path.Replace('/', '\\').TrimEnd('\\').Split('\\')[^1];

    /// <summary>A Windows path's file name without its extension, on any OS the tests run on.</summary>
    internal static string FileTitle(string path)
    {
        var name = FileName(path);
        return name.LastIndexOf('.') is > 0 and var dot ? name[..dot] : name;
    }

    /// <summary>One game on the list: a game found on this PC, or a profile whose game is no longer found.</summary>
    public sealed class GameRow
    {
        private GameRow(InstalledGame? game, string? exePath, bool hasProfile)
        {
            Game = game;
            ExePath = exePath;
            HasProfile = hasProfile;
            Name = game?.Name ?? FileTitle(exePath!);
            Launcher = game is null ? "" : LauncherName(game.Launcher);
            Detail = game is null
                ? "Not in your games any more"
                : string.Join(" · ", game.AntiCheats.Select(AntiCheatWarning.Name).Prepend(Launcher));
        }

        /// <summary>The game as the service found it, or null for a profile whose game is no longer found.</summary>
        public InstalledGame? Game { get; }

        public string Name { get; }

        /// <summary>Where it was found: "Steam", or "Added by you". Empty for a profile whose game is no longer found.</summary>
        public string Launcher { get; }

        /// <summary>Where it was found and its anti-cheats: "Epic Games · Easy Anti-Cheat".</summary>
        public string Detail { get; }

        /// <summary>
        /// The .exe its profile is for: the one its launcher gave, else the one its saved profile was made for. Null
        /// while neither is known (Steam and Battle.net give only a game's folder).
        /// </summary>
        public string? ExePath { get; }

        public bool HasProfile { get; }

        /// <summary>Only games added by hand can be removed; launcher games go when the launcher uninstalls them.</summary>
        public bool CanRemove => Game?.Launcher == GameLauncher.Manual;

        /// <summary>Whether this is the same game as a row of a list loaded earlier.</summary>
        public bool IsSameGame(GameRow other) =>
            Game is { } game ? game.Equals(other.Game) : other.Game is null && WinPathSame(ExePath!, other.ExePath!);

        /// <summary>
        /// The list: each game with its profile, which is the one for its .exe or, for a game its launcher gave no .exe
        /// for, the one for an .exe in its folder (as the service matches them); then each profile no game has. By name.
        /// </summary>
        public static IReadOnlyList<GameRow> All(IReadOnlyList<InstalledGame> games, IReadOnlyList<GameProfile> profiles)
        {
            var rows = games
                .Select(game => (Game: game, Profile: profiles.FirstOrDefault(p => game.ExePath is { } exe
                    ? WinPathSame(p.ExePath, exe)
                    : IsInside(p.ExePath, game.InstallPath))))
                .Select(g => new GameRow(g.Game, g.Game.ExePath ?? g.Profile?.ExePath, g.Profile is not null))
                .ToList();
            var orphans = profiles
                .Where(p => !rows.Any(r => r.HasProfile && WinPathSame(r.ExePath!, p.ExePath)))
                .Select(p => new GameRow(null, p.ExePath, hasProfile: true));
            return [.. rows.Concat(orphans).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)];
        }

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

/// <summary>
/// A pick-one choice in a profile (the power plan): "No change", which leaves it as it is while the game runs, or one
/// of its options. Shown as a segmented choice, like its row on its own page.
/// </summary>
public sealed class ProfileChoice : ObservableObject
{
    /// <param name="optionIds">The options a profile can hold on this PC, in any order.</param>
    public ProfileChoice(ChoiceGroup choice, IReadOnlyList<string> optionIds)
    {
        Name = choice.Name;
        Options =
        [
            new(this, null, "No change"),
            .. choice.Options.Where(o => optionIds.Contains(o.TweakId)).Select(o => new ProfileChoiceOption(this, o.TweakId, o.Label)),
        ];
        Options[0].IsSelected = true;
    }

    /// <summary>The choice's name, e.g. "Power plan".</summary>
    public string Name { get; }

    /// <summary>"No change", then each option, as the segments show them.</summary>
    public IReadOnlyList<ProfileChoiceOption> Options { get; }

    /// <summary>The option the profile holds, or null for "No change".</summary>
    public string? ChosenTweakId => Options.Single(o => o.IsSelected).TweakId;

    public void Choose(ProfileChoiceOption chosen)
    {
        foreach (var option in Options)
            option.IsSelected = option == chosen;
    }

    /// <summary>Selects the option a saved profile holds, or "No change" when it holds none.</summary>
    public void Show(IReadOnlyList<string> tweakIds) =>
        Choose(Options.Skip(1).FirstOrDefault(o => tweakIds.Contains(o.TweakId!)) ?? Options[0]);
}

/// <summary>One segment of a <see cref="ProfileChoice"/>.</summary>
public sealed class ProfileChoiceOption(ProfileChoice choice, string? tweakId, string label) : ObservableObject
{
    private bool _isSelected;

    /// <summary>The option's tweak, or null for "No change".</summary>
    public string? TweakId { get; } = tweakId;

    public string Label { get; } = label;

    /// <summary>The one the profile holds; the segment shows it in the button wave.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => Set(ref _isSelected, value);
    }

    public void Choose() => choice.Choose(this);
}

/// <summary>A background program the profile closes while the game runs, shown by name with its path under it.</summary>
public sealed class AppToClose(string path)
{
    /// <summary>The program's .exe, as the profile stores it.</summary>
    public string Path { get; } = path;

    /// <summary>The program's own description ("Discord"), else its file name.</summary>
    public string Name { get; } = Describe(path);

    private static string Describe(string path)
    {
        try
        {
            if (FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
                return description.Trim();
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException or IOException)
        {
            // Gone or unreadable: the file name still says which program it is.
        }
        return ProfilesPageViewModel.FileTitle(path);
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
