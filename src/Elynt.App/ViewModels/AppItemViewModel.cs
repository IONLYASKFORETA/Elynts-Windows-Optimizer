using Elynt.Core.Apps;

namespace Elynt.App.ViewModels;

/// <summary>One catalogue app on the Apps page: a tick to queue it, whether it is installed, and its install status.</summary>
public sealed class AppItemViewModel(AppView view) : ObservableObject
{
    private bool _isTicked;
    private string? _status;
    private InstalledAppView? _installed;

    public AppView View { get; } = view;

    public string Name => View.Name;

    public string Group => View.Group;

    public string Description => View.Description;

    public bool IsTicked
    {
        get => _isTicked;
        set => Set(ref _isTicked, value);
    }

    /// <summary>
    /// What winget says of this app, as a pill on its row: "Installed", "Update available", or null while it isn't
    /// installed (or the installed list hasn't been read).
    /// </summary>
    public string? InstalledText => _installed switch
    {
        null => null,
        { CanUpdate: true } => "Update available",
        _ => "Installed",
    };

    /// <summary>A newer version waits in winget's community source, so the pill is orange.</summary>
    public bool HasUpdate => _installed?.CanUpdate == true;

    /// <summary>Shows what winget lists for this app: its installed copy, or null when it isn't installed.</summary>
    public void ShowInstalled(InstalledAppView? installed)
    {
        if (Set(ref _installed, installed, nameof(InstalledText)))
            OnPropertyChanged(nameof(HasUpdate));
    }

    /// <summary>"Queued", "Installing… 40%", "Installed" and so on, while and after the queue runs.</summary>
    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }
}
