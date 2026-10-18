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

    public string TierText => View.Tier switch
    {
        EvidenceTier.Proven => "Proven",
        EvidenceTier.Situational => "Situational",
        _ => "Possible placebo",
    };

    public string ReversibilityText => View.Reversibility switch
    {
        Reversibility.Reversible => "Reversible",
        Reversibility.NeedsDownload => "Undo needs a download",
        _ => "Destructive",
    };

    /// <summary>The list row's label line, e.g. "Proven · Reversible · Needs reboot".</summary>
    public string LabelLine => string.Join(" · ", new[] { TierText, ReversibilityText }.Concat(RiskBadges));

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

    public string RiskText => RiskBadges.Count == 0 ? "None" : string.Join(", ", RiskBadges);

    /// <summary>One line per objecting anti-cheat for the detail pane. Warns; never blocks.</summary>
    public IReadOnlyList<string> AntiCheatWarnings => View.AntiCheat
        .Select(c => $"{AntiCheatName(c.AntiCheat)}: {SeverityText(c.Severity)} ({c.Detail})")
        .ToList();

    public bool HasAntiCheatWarnings => View.AntiCheat.Count > 0;

    /// <summary>Names the installed games this tweak would affect, e.g. "... may stop VALORANT (Riot Vanguard) from launching."</summary>
    public string? GamesWarning => View.AntiCheatWarning;

    public bool HasGamesWarning => View.AntiCheatWarning is not null;

    public bool IsDestructive => View.Reversibility == Reversibility.Destructive;

    public bool HasWarning => View.Warning is not null;

    public string StateText => !View.Applicable && View.State == TweakState.Off
        ? "Not applicable"
        : (View.State == TweakState.On ? "On" : "Off") + (View.PendingReboot ? " · pending reboot" : "");

    public bool PendingReboot => View.PendingReboot;

    public bool IsNotApplicable => !View.Applicable;

    public string? NotApplicableReason => View.NotApplicableReason;

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

    /// <summary>A not-applicable tweak can only be switched off, and only when it has something to undo.</summary>
    public bool IsEnabled => !IsBusy && (View.Applicable || View.CanUndo);

    /// <summary>The service's answer to the last flip, shown in the detail pane.</summary>
    public string? Message
    {
        get => _message;
        set => Set(ref _message, value);
    }

    /// <summary>Makes the switch re-read <see cref="IsOn"/>, e.g. after a refused flip.</summary>
    public void RefreshSwitch() => OnPropertyChanged(nameof(IsOn));
}
