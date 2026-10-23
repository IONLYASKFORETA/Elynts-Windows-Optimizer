using System.Windows;
using System.Windows.Controls;
using Elynt.App.ViewModels;

namespace Elynt.App.Pages;

/// <summary>Cleanup's scan and clean, above the section's tweaks. Its DataContext is the window's <see cref="MainViewModel"/>.</summary>
public partial class CleanupPanel : UserControl
{
    public CleanupPanel() => InitializeComponent();

    private MainViewModel Model => (MainViewModel)DataContext;

    private async void ScanCleanup(object sender, RoutedEventArgs e) => await Model.Cleanup.ScanAsync();

    private async void CleanTicked(object sender, RoutedEventArgs e) => await Model.Cleanup.CleanAsync();
}
