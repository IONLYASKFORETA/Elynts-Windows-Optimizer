using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Elynt.App.ViewModels;

namespace Elynt.App.Pages;

/// <summary>The Drivers page: GPU and chipset drivers, then driver cleaning. Its DataContext is the <see cref="MainViewModel"/>.</summary>
public partial class DriversPage : UserControl
{
    public DriversPage() => InitializeComponent();

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

    private async void CleanDrivers(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DriverCleaningViewModel cleaning)
            await cleaning.CleanAsync();
    }

    private async void DeepCleanDrivers(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DriverCleaningViewModel cleaning)
            await cleaning.DeepCleanAsync();
    }

    private async void RestoreDrivers(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DriverBackupItemViewModel backup)
            await backup.RestoreAsync();
    }

    private void OpenLink(object sender, RequestNavigateEventArgs e) => Links.Open(e);
}
