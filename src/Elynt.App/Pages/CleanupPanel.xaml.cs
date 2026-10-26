using System.Windows;
using System.Windows.Controls;
using Elynt.App.ViewModels;

namespace Elynt.App.Pages;

/// <summary>Cleanup's scan and clean, above the section's tweaks. Its DataContext is the window's <see cref="MainViewModel"/>.</summary>
public partial class CleanupPanel : UserControl
{
    public CleanupPanel()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Parent is not FrameworkElement page)
                return;
            page.SizeChanged -= PageSizeChanged;
            page.SizeChanged += PageSizeChanged;
            FitCategories(page.ActualHeight);
        };
    }

    private MainViewModel Model => (MainViewModel)DataContext;

    private async void ScanCleanup(object sender, RoutedEventArgs e) => await Model.Cleanup.ScanAsync();

    private async void CleanTicked(object sender, RoutedEventArgs e) => await Model.Cleanup.CleanAsync();

    private void PageSizeChanged(object sender, SizeChangedEventArgs e) => FitCategories(e.NewSize.Height);

    /// <summary>
    /// Holds the category list to half of the page it shares with the section's tweaks, so that after a scan the
    /// tweaks underneath still have room at the smallest window. The list scrolls within that.
    /// </summary>
    private void FitCategories(double pageHeight)
    {
        if (pageHeight > 0)
            CategoryList.MaxHeight = pageHeight / 2;
    }
}
