using Elynt.Core;
using Elynt.Core.Tweaks;

namespace Elynt.App.ViewModels;

/// <summary>One row in the tweak list. Flipping <see cref="IsOn"/> applies or undoes the tweak straight away.</summary>
public sealed class TweakItemViewModel(TweakView view, Func<TweakItemViewModel, bool, Task> flip) : ObservableObject
{
    private TweakView _view = view;
    private bool _isBusy;
    private string? _message;

    public TweakView View
    {
        get => _view;
        set
        {
            if (Set(ref _view, value))
            {
                // Every label is derived from the view, so refresh them all.
                OnPropertyChanged(string.Empty);
            }
        }
    }

    public string Name => View.Name;

    /// <summary>One plain sentence under the name, so the row says what it does without being selected.</summary>
    public string? Summary => View.Summary;

    public string TierText => View.Tier switch
    {
        EvidenceTier.Proven => "Proven",
        EvidenceTier.Situational => "Situational",
        _ => "Possible placebo",
    };

    /// <summary>The page's rows with no header of their own, listed first.</summary>
    public const string MainGroup = "Tweaks";
    public const string PlaceboGroup = "Possible placebo";

    /// <summary>Possible-placebo tweaks are listed in their own group, folded at the bottom of the page.</summary>
    public bool IsPlacebo => View.Tier == EvidenceTier.PossiblePlacebo;

    /// <summary>
    /// The group the list puts the row in: the placebo fold, else the header the catalogue gives the tweak (such as
    /// "Telemetry"), else the page's plain rows.
    /// </summary>
    public string Group => IsPlacebo ? PlaceboGroup : View.Group ?? MainGroup;

    /// <summary>
    /// A page's rows in the order the list shows them: the plain rows, then each header's rows in the order the
    /// catalogue first lists that header, then the placebo fold. A grouped list shows its groups in the order of their
    /// first row, so this is what puts the headers in order. Within each group, rows that fit this PC come first.
    /// </summary>
    public static IReadOnlyList<TweakItemViewModel> InPageOrder(IEnumerable<TweakItemViewModel> rows)
    {
        var listed = rows.ToList();
        var groups = listed.Select(row => row.Group).Distinct().ToList();
        return listed
            .OrderBy(row => row.IsPlacebo)
            .ThenBy(row => row.Group == MainGroup ? -1 : groups.IndexOf(row.Group))
            .ThenBy(row => row.IsNotApplicable)
            .ToList();
    }

    public string ReversibilityText => View.Reversibility switch
    {
        Reversibility.Reversible => "Reversible",
        Reversibility.NeedsDownload => "Undo needs a download",
        _ => "Destructive",
    };

    /// <summary>Every risk flag, plus one badge per anti-cheat that objects.</summary>
    public IReadOnlyList<string> RiskBadges
    {
        get
        {
            var badges = new List<string>();
            if (View.Risks.HasFlag(RiskFlags.SecurityRisk)) badges.Add("Security risk");
            if (View.Risks.HasFlag(RiskFlags.NeedsReboot)) badges.Add("Needs reboot");
            if (View.Risks.HasFlag(RiskFlags.CanStopBoot)) badges.Add("Can stop boot");
            badges.AddRange(View.AntiCheat.Select(c => $"Anti-cheat: {AntiCheatName(c.AntiCheat)}"));
            return badges;
        }
    }

    /// <summary>One line per objecting anti-cheat for the detail pane. Warns; never blocks.</summary>
    public IReadOnlyList<string> AntiCheatWarnings => View.AntiCheat
        .Select(c => $"{AntiCheatName(c.AntiCheat)}: {SeverityText(c.Severity)} ({c.Detail})")
        .ToList();

    public bool HasAntiCheatWarnings => View.AntiCheat.Count > 0;

    /// <summary>Names the installed games this tweak would affect, e.g. "... may stop VALORANT (Riot Vanguard) from launching."</summary>
    public string? GamesWarning => View.AntiCheatWarning;

    public bool HasGamesWarning => View.AntiCheatWarning is not null;

    public bool IsDestructive => View.Reversibility == Reversibility.Destructive;

    public bool HasApplyWarning => View.ApplyWarning is not null;

    /// <summary>Warnings for this PC's hardware, such as the X3D core-parking and Frame Generation notes.</summary>
    public IReadOnlyList<string> Warnings => View.Warnings;

    public bool HasWarnings => View.Warnings.Count > 0;

    /// <summary>A per-game choice: instead of a switch it offers "Choose a game…".</summary>
    public bool IsPerGame => View.PerGame;

    public bool HasSwitch => !View.PerGame;

    /// <summary>The state beside the detail pane's switch: "On", "Off", "Per game", "Not applicable".</summary>
    public string StateText => View.PerGame ? "Per game" : !View.Applicable && View.State == TweakState.Off
        ? "Not applicable"
        : (View.State == TweakState.On ? "On" : "Off") + (View.PendingReboot ? " · pending reboot" : "");

    /// <summary>
    /// What the row says beside its labels, or null for nothing. Its switch already shows on or off, so only what the
    /// switch can't say.
    /// </summary>
    public string? RowStateText => View.PerGame ? "Per game"
        : !View.Applicable && View.State == TweakState.Off ? "Not applicable"
        : View.PendingReboot ? "Pending reboot"
        : null;

    public bool HasRowStateText => RowStateText is not null;

    public bool PendingReboot => View.PendingReboot;

    public bool IsNotApplicable => !View.Applicable;

    public string? NotApplicableReason => View.NotApplicableReason;

    /// <summary>A transient block the user clears by hand (e.g. Defender's Tamper Protection), with a how-to guide.</summary>
    public bool IsBlocked => View.Blocked;

    public string? BlockedReason => View.BlockedReason;

    /// <summary>
    /// Why the switch is locked, as the label on the row and in the detail pane reads, or null when it isn't. Every
    /// lock's words live here and nowhere in the XAML, so another lock adds its own case to this one switch.
    /// </summary>
    public string? LockText => View switch
    {
        // On because something other than the app set it: the app has nothing of its own to undo.
        { AlreadySet: true } => "Already set on this PC",
        // On by the app's own destructive change, which has nothing to put back, so it stays on (ADR 0002).
        { Reversibility: Reversibility.Destructive, State: TweakState.On } => CantBeUndoneText,
        _ => null,
    };

    /// <summary>The words for a destructive change that is in place, on its switch and on its Undo history row alike.</summary>
    public const string CantBeUndoneText = "Can't be undone";

    /// <summary>The switch can't be flipped either way; <see cref="LockText"/> says why.</summary>
    public bool IsLocked => LockText is not null;

    /// <summary>
    /// The lock is the app's own destructive change, so its label is the red danger pill, like "Destructive". A tweak
    /// already set on this PC is locked too, but plainly: nothing the app did is beyond undoing.
    /// </summary>
    public bool CantBeUndone => LockText == CantBeUndoneText;

    private static string AntiCheatName(AntiCheat antiCheat) => antiCheat switch
    {
        AntiCheat.Vanguard => "Riot Vanguard",
        AntiCheat.FaceIt => "FACEIT",
        AntiCheat.EasyAntiCheat => "Easy Anti-Cheat",
        AntiCheat.BattlEye => "BattlEye",
        AntiCheat.Ricochet => "Ricochet",
        AntiCheat.Javelin => "EA Javelin",
        _ => antiCheat.ToString(),
    };

    private static string SeverityText(AntiCheatSeverity severity) => severity switch
    {
        AntiCheatSeverity.WontLaunch => "games won't launch",
        AntiCheatSeverity.Restricted => "games run with restrictions",
        AntiCheatSeverity.Kick => "may kick you from a match",
        _ => "blocks the tool, the game still runs",
    };

    /// <summary>The detected state. Setting it sends the command; the switch then shows what the service detects.</summary>
    public bool IsOn
    {
        get => View.State == TweakState.On;
        set
        {
            if (value != IsOn && !IsBusy)
                _ = flip(this, value);
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (Set(ref _isBusy, value))
                OnPropertyChanged(nameof(IsEnabled));
        }
    }

    /// <summary>
    /// A locked switch (see <see cref="LockText"/>) can't be flipped at all, whether or not the tweak fits this PC or is
    /// blocked. A not-applicable tweak can only be switched off, and only when it has something to undo. A blocked tweak
    /// (e.g. Defender off while Tamper Protection is on) can't be turned on until the user clears the block by hand, but
    /// can still be turned back off when it is on.
    /// </summary>
    public bool IsEnabled => !IsBusy && !IsLocked && (View.Applicable || View.CanUndo) &&
        (!IsBlocked || View.CanUndo || View.State == TweakState.On);

    /// <summary>The service's answer to the last flip, shown in the detail pane.</summary>
    public string? Message
    {
        get => _message;
        set => Set(ref _message, value);
    }

    /// <summary>Makes the switch re-read <see cref="IsOn"/>, e.g. after a refused flip.</summary>
    public void RefreshSwitch() => OnPropertyChanged(nameof(IsOn));
}
