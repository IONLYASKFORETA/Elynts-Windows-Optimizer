using System.ComponentModel;
using Elynt.App.ViewModels;

namespace Elynt.App.Prototype;

/// <summary>
/// PROTOTYPE, throwaway. Navigation (Home first, then the real sections) and the Home page's figures, shared by
/// the three layout variants. Everything else comes from the real <see cref="MainViewModel"/>.
/// </summary>
public sealed class PrototypeViewModel : ObservableObject
{
    public const string Home = "Home";

    private NavItem _selectedNav;

    public PrototypeViewModel(MainViewModel model)
    {
        Model = model;
        Nav = [new NavItem(Home, Glyphs.For(Home)), .. model.Pages.Select(p => new NavItem(p, Glyphs.For(p)))];
        _selectedNav = Nav[0];
        model.PropertyChanged += OnModelChanged;
    }

    public MainViewModel Model { get; }

    public IReadOnlyList<NavItem> Nav { get; }

    public NavItem SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (value is null)
                return;
            Set(ref _selectedNav, value);
            if (!value.IsHome)
                Model.SelectedPage = value.Page;
            Refresh();
        }
    }

    public void Open(string page) => SelectedNav = Nav.First(n => n.Page == page);

    public bool IsHome => SelectedNav.IsHome;

    public bool IsTweakSection => !IsHome && Model.IsTweakPage;

    public bool IsOtherSection => !IsHome && !Model.IsTweakPage;

    public bool HasTweakDetail => IsTweakSection && Model.SelectedTweak is not null;

    public string PageTitle => SelectedNav.Page;

    public string PageSummary =>
        IsHome ? "Your PC at a glance" :
        IsTweakSection ? $"{Model.Tweaks.Count(t => t.IsOn)} of {Model.Tweaks.Count} on · switch any of them off again at any time" :
        "Tools and panels for this section";

    // Home page figures.

    public string Hardware => Model.Bios?.Hardware ?? "Reading this PC's hardware…";

    public string WindowsVersion => "Windows 11 Pro 24H2";

    public int TweaksOn => Model.AllTweaks.Count(t => t.IsOn);

    public int TweaksAvailable => Model.AllTweaks.Count(t => !t.IsNotApplicable);

    public int RestartsWaiting => Model.AllTweaks.Count(t => t.PendingReboot);

    public string TemperatureText => Model.Sensors?.Installed == true ? "CPU 54 °C · GPU 47 °C" : "Turn on PawnIO for CPU temperature";

    public IReadOnlyList<SectionSummary> Sections =>
        Elynt.Core.Tweaks.Sections.All.Select(section =>
        {
            var tweaks = Model.AllTweaks.Where(t => t.View.Section == section && !t.IsNotApplicable).ToList();
            return new SectionSummary(section, Glyphs.For(section), tweaks.Count(t => t.IsOn), tweaks.Count);
        }).ToList();

    public IReadOnlyList<string> NextSteps =>
    [
        .. RestartsWaiting > 0 ? [$"Restart to finish {RestartsWaiting} change{(RestartsWaiting == 1 ? "" : "s")}"] : Array.Empty<string>(),
        "Check for a newer GPU driver",
        "Scan for files that are safe to clean up",
    ];

    public void Refresh() => OnPropertyChanged(string.Empty);

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedTweak) or nameof(MainViewModel.IsTweakPage)
            or nameof(MainViewModel.IsConnected) or nameof(MainViewModel.Bios) or nameof(MainViewModel.Sensors))
            Refresh();
    }
}

public sealed record NavItem(string Page, string Glyph)
{
    public bool IsHome => Page == PrototypeViewModel.Home;
}

public sealed record SectionSummary(string Name, string Glyph, int On, int Total)
{
    public string Status => Total == 0 ? "Tools" : $"{On} of {Total} on";

    public double Fraction => Total == 0 ? 0 : (double)On / Total;
}

/// <summary>Segoe Fluent Icons code points (shared with Segoe MDL2 Assets).</summary>
public static class Glyphs
{
    public static string For(string page) => page switch
    {
        PrototypeViewModel.Home => "",
        Elynt.Core.Tweaks.Sections.Gaming => "",
        Elynt.Core.Tweaks.Sections.DebloatAndPrivacy => "",
        Elynt.Core.Tweaks.Sections.Security => "",
        Elynt.Core.Tweaks.Sections.Apps => "",
        Elynt.Core.Tweaks.Sections.WindowsUpdate => "",
        Elynt.Core.Tweaks.Sections.StartupAndServices => "",
        Elynt.Core.Tweaks.Sections.Cleanup => "",
        Elynt.Core.Tweaks.Sections.Repair => "",
        Elynt.Core.Tweaks.Sections.Drivers => "",
        Elynt.Core.Tweaks.Sections.Overclocking => "",
        MainViewModel.PerGameProfiles => "",
        MainViewModel.UndoHistory => "",
        _ => "",
    };
}
