using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Elynt.App.ViewModels;
using Elynt.Core;
using Elynt.Core.Drivers;

namespace Elynt.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    public MainWindow()
    {
        _model = new MainViewModel(ConfirmDestructive, AskToReboot);
        InitializeComponent();
        DataContext = _model;
        Loaded += async (_, _) => await _model.ConnectAsync();
    }

    private async void Retry(object sender, RoutedEventArgs e) => await _model.ConnectAsync();

    /// <summary>The separate confirmation a destructive tweak needs. "No" is the default button.</summary>
    private bool ConfirmDestructive(TweakView tweak) =>
        MessageBox.Show(
            this,
            $"{tweak.Name}\n\nThis can't be undone. {tweak.DestructiveWarning}\n\nDo it anyway?",
            "This can't be undone",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    private async void CheckDriver(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GpuItemViewModel gpu)
            await gpu.CheckAsync();
    }

    private async void InstallDriver(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GpuItemViewModel gpu)
            await gpu.InstallAsync();
    }

    /// <summary>The reboot prompt after a driver installer reported that Windows must restart. "Later" is the default.</summary>
    private void AskToReboot(DriverInstallResult result)
    {
        var answer = MessageBox.Show(
            this,
            $"{result.Message}\n\nRestart now? Save your work in other apps first.",
            "Restart to finish the driver install",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = false, CreateNoWindow = true });
    }

    private void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
