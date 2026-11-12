using System.Collections.ObjectModel;
using System.ComponentModel;
using Elynt.Core.Apps;

namespace Elynt.App.ViewModels;

/// <summary>
/// The Apps page's Catalogue tab: every reviewed app, the filter over them, which of them winget lists as installed,
/// and what the install button will do. The window's model runs the install itself.
/// </summary>
public sealed class AppCatalogueViewModel : ObservableObject
{
    private string _filter = "";
    private bool _installAllRuntimes;
    private AppItemViewModel? _selected;

    /// <summary>Every catalogue app, in group order.</summary>
    public ObservableCollection<AppItemViewModel> Apps { get; } = [];

    /// <summary>The apps the filter matches, in group order: the rows the tab draws.</summary>
    public ObservableCollection<AppItemViewModel> Shown { get; } = [];

    /// <summary>Part of an app's name or description, typed into the search box over the list.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value))
                ShowMatches();
        }
    }

    /// <summary>The selected row: the one the sidebar's search opened, or the last one clicked.</summary>
    public AppItemViewModel? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    /// <summary>
    /// Selects an app's row, as the sidebar's search opens it. A filter that hides the row is cleared so the row shows;
    /// one that shows it stays. An app the catalogue doesn't have changes nothing.
    /// </summary>
    public void Select(string appId)
    {
        if (Apps.FirstOrDefault(a => string.Equals(a.View.Id, appId, StringComparison.OrdinalIgnoreCase)) is not { } app)
            return;
        if (!Shown.Contains(app))
            Filter = "";
        if (Selected == app)
            // Said again, so the page brings the row back into view even though it was already selected.
            OnPropertyChanged(nameof(Selected));
        else
            Selected = app;
    }

    /// <summary>How many apps the filter shows, under the search box: "12 apps", or why there are none.</summary>
    public string ResultCount => Shown.Count switch
    {
        0 when Filter.Trim().Length > 0 => $"No apps match \"{Filter.Trim()}\".",
        1 => "1 app",
        var count => $"{count} apps",
    };

    /// <summary>Adds every Visual C++, DirectX legacy and .NET runtime to the install queue.</summary>
    public bool InstallAllRuntimes
    {
        get => _installAllRuntimes;
        set
        {
            if (Set(ref _installAllRuntimes, value))
                CountChanged();
        }
    }

    /// <summary>
    /// How many apps the install button installs: the ticked ones, hidden by the filter or not, and every runtime when
    /// <see cref="InstallAllRuntimes"/> is on, each once.
    /// </summary>
    public int InstallCount => Apps.Count(a => a.IsTicked || (InstallAllRuntimes && a.View.InAllRuntimes));

    /// <summary>The install button's words: "Install 3 apps".</summary>
    public string InstallText => InstallButtonText(InstallCount);

    /// <summary>The install button is off while it would install nothing.</summary>
    public bool HasAppsToInstall => InstallCount > 0;

    /// <summary>An install button's words for this many apps: "Install 3 apps", or "Install apps" for none.</summary>
    public static string InstallButtonText(int count) => count switch
    {
        0 => "Install apps",
        1 => "Install 1 app",
        _ => $"Install {count} apps",
    };

    /// <summary>Replaces the rows with the service's catalogue.</summary>
    public void Show(IReadOnlyList<AppView> apps)
    {
        foreach (var app in Apps)
            app.PropertyChanged -= OnAppChanged;
        Apps.Clear();
        foreach (var app in apps)
        {
            var row = new AppItemViewModel(app);
            row.PropertyChanged += OnAppChanged;
            Apps.Add(row);
        }
        ShowMatches();
        CountChanged();
    }

    private void OnAppChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppItemViewModel.IsTicked))
            CountChanged();
    }

    private void CountChanged()
    {
        OnPropertyChanged(nameof(InstallCount));
        OnPropertyChanged(nameof(InstallText));
        OnPropertyChanged(nameof(HasAppsToInstall));
    }

    private void ShowMatches()
    {
        var search = Filter.Trim();
        Shown.Clear();
        foreach (var app in Apps.Where(a => Matches(a.Name) || Matches(a.Description)))
            Shown.Add(app);
        OnPropertyChanged(nameof(ResultCount));

        bool Matches(string text) => text.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Marks each row from winget's installed list, matched by winget id. winget doesn't keep an id's case, so neither
    /// does the match.
    /// </summary>
    public void MarkInstalled(IReadOnlyList<InstalledAppView> installed)
    {
        var byId = installed
            .DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var app in Apps)
            app.ShowInstalled(byId.GetValueOrDefault(app.View.Id));
    }
}
