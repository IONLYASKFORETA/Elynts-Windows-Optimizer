using Elynt.Core.Startup;

namespace Elynt.App.ViewModels;

/// <summary>One startup item on the Startup page: its details and its on/off switch.</summary>
public sealed class StartupItemViewModel(StartupItemView view, Func<StartupItemViewModel, bool, Task> flip) : ObservableObject
{
    private StartupItemView _view = view;
    private bool _isBusy;
    private string? _message;

    public StartupItemView View
    {
        get => _view;
        set
        {
            if (Set(ref _view, value))
            {
                OnPropertyChanged(nameof(IsOn));
                OnPropertyChanged(nameof(Details));
                OnPropertyChanged(nameof(IsEnabled));
            }
        }
    }

    public string Name => View.Name;

    public string SourceText => View.Source switch
    {
        StartupSource.RunKey => "Run key",
        StartupSource.StartupFolder => "Startup folder",
        StartupSource.SignInTask => "Sign-in task",
        StartupSource.StoreApp => "Store app",
        _ => "Scheduled task",
    };

    /// <summary>The group the page lists it under: startup items first, then other scheduled tasks.</summary>
    public string Group => View.Source == StartupSource.ScheduledTask ? "Third-party scheduled tasks" : "Starts at sign-in";

    public string Details
    {
        get
        {
            var signature = View.Signature switch
            {
                SignatureStatus.Signed => "signed",
                SignatureStatus.Unsigned => "not signed",
                SignatureStatus.Invalid => "signature broken",
                _ => "signature unknown",
            };
            var impact = View.Impact == StartupImpact.NotMeasured ? "impact not measured" : $"{View.Impact.ToString().ToLowerInvariant()} impact";
            return $"{View.Publisher ?? "Unknown publisher"} · {signature} · {impact} · {SourceText}";
        }
    }

    public string Location => View.Location;

    public string Command => View.Command;

    public bool CanDelete => View.CanDelete && !IsBusy;

    public bool IsEnabled => View.CanSwitch && !IsBusy;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (Set(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(CanDelete));
            }
        }
    }

    public string? Message
    {
        get => _message;
        set => Set(ref _message, value);
    }

    /// <summary>The switch. Flipping it asks the service; the switch then shows what the service reports.</summary>
    public bool IsOn
    {
        get => View.Enabled;
        set
        {
            if (value != View.Enabled && !IsBusy)
                _ = flip(this, value);
        }
    }

    public void RefreshSwitch() => OnPropertyChanged(nameof(IsOn));
}
