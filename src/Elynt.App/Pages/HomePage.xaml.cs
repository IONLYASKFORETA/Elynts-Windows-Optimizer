using System.Windows;
using System.Windows.Controls;
using Elynt.App.ViewModels;
using Elynt.Core.Tweaks;

namespace Elynt.App.Pages;

/// <summary>
/// The Home page. Its DataContext is the window's <see cref="MainViewModel"/>. While the page is on screen (shown,
/// and the window not minimised or hidden) its temperatures refresh; the cards re-flow to the page's width.
/// </summary>
public partial class HomePage : UserControl
{
    public static readonly DependencyProperty StatColumnsProperty =
        DependencyProperty.Register(nameof(StatColumns), typeof(int), typeof(HomePage), new PropertyMetadata(4));

    public static readonly DependencyProperty SectionColumnsProperty =
        DependencyProperty.Register(nameof(SectionColumns), typeof(int), typeof(HomePage), new PropertyMetadata(5));

    private Window? _window;

    public HomePage()
    {
        InitializeComponent();
        SizeChanged += (_, e) => FitColumns(e.NewSize.Width);
        IsVisibleChanged += (_, _) => UpdateActive();
        Loaded += (_, _) =>
        {
            _window = Window.GetWindow(this);
            if (_window is not null)
                _window.StateChanged += OnWindowStateChanged;
            UpdateActive();
        };
        Unloaded += (_, _) =>
        {
            if (_window is not null)
                _window.StateChanged -= OnWindowStateChanged;
            _window = null;
        };
    }

    /// <summary>Stat cards per row: four, or two when the page is narrow.</summary>
    public int StatColumns
    {
        get => (int)GetValue(StatColumnsProperty);
        set => SetValue(StatColumnsProperty, value);
    }

    /// <summary>Section cards per row: five on a wide page, fewer as it narrows.</summary>
    public int SectionColumns
    {
        get => (int)GetValue(SectionColumnsProperty);
        set => SetValue(SectionColumnsProperty, value);
    }

    private MainViewModel? Model => DataContext as MainViewModel;

    private void FitColumns(double width)
    {
        StatColumns = width >= 700 ? 4 : 2;
        SectionColumns = width >= 1100 ? 5 : width >= 700 ? 3 : 2;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateActive();

    private void UpdateActive()
    {
        if (Model is { } model)
            model.HomePage.Active = IsVisible && _window?.WindowState != WindowState.Minimized;
    }

    private async void CleanUp(object sender, RoutedEventArgs e)
    {
        if (Model is { } model)
            await model.HomePage.CleanUpAsync();
    }

    private async void CheckDrivers(object sender, RoutedEventArgs e)
    {
        if (Model is { } model)
            await model.HomePage.CheckDriversAsync();
    }

    private void OpenWaiting(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HomeViewModel.WaitingItem item)
            Model?.HomePage.Open(item.Page);
    }

    private void OpenSection(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HomeViewModel.SectionCard card)
            Model?.HomePage.Open(card.Section);
    }

    private void OpenOverclocking(object sender, RoutedEventArgs e) => Model?.HomePage.Open(Sections.Overclocking);
}
