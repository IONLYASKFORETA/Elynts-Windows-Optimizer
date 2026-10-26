using System.Windows;
using System.Windows.Controls;
using Elynt.App.ViewModels;

namespace Elynt.App.Pages;

/// <summary>Startup & Services: the startup list and the services list. Its DataContext is the <see cref="MainViewModel"/>.</summary>
public partial class StartupAndServicesPage : UserControl
{
    public StartupAndServicesPage() => InitializeComponent();

    private MainViewModel Model => (MainViewModel)DataContext;

    private async void SetServiceStart(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ServiceItemViewModel item && Model.ServicesPage is { } page)
            await page.SetAsync(item);
    }

    private async void UndoServiceChange(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ServiceItemViewModel item && Model.ServicesPage is { } page)
            await page.UndoAsync(item);
    }
}
