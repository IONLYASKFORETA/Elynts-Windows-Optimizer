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
                OnPropertyChanged(nameof(IsOn));
                OnPropertyChanged(nameof(StateText));
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

    public string StateText => View.State == TweakState.On ? "On" : "Off";

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

    public bool IsEnabled => !IsBusy;

    /// <summary>The service's answer to the last flip, shown in the detail pane.</summary>
    public string? Message
    {
        get => _message;
        set => Set(ref _message, value);
    }

    /// <summary>Makes the switch re-read <see cref="IsOn"/>, e.g. after a refused flip.</summary>
    public void RefreshSwitch() => OnPropertyChanged(nameof(IsOn));
}
