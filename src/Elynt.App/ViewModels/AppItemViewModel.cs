using Elynt.Core.Apps;

namespace Elynt.App.ViewModels;

/// <summary>One catalogue app on the Apps page: a tick to queue it, and its install status.</summary>
public sealed class AppItemViewModel(AppView view) : ObservableObject
{
    private bool _isTicked;
    private string? _status;

    public AppView View { get; } = view;

    public string Name => View.Name;

    public string Group => View.Group;

    public string Description => View.Description;

    public bool IsTicked
    {
        get => _isTicked;
        set => Set(ref _isTicked, value);
    }

    /// <summary>"Queued", "Installing… 40%", "Installed" and so on, while and after the queue runs.</summary>
    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }
}
