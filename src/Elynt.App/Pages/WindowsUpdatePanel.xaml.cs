using System.Windows;
using System.Windows.Controls;
using Elynt.App.ViewModels;

namespace Elynt.App.Pages;

/// <summary>Windows Update's install-now button and progress. Its DataContext is the window's <see cref="MainViewModel"/>.</summary>
public partial class WindowsUpdatePanel : UserControl
{
    public WindowsUpdatePanel() => InitializeComponent();

    private MainViewModel Model => (MainViewModel)DataContext;

    private async void CheckAndInstallWindowsUpdates(object sender, RoutedEventArgs e) =>
        await Model.WindowsUpdate.StartAsync();
}
