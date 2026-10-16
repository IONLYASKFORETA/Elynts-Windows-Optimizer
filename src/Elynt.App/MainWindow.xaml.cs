using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Elynt.App.ViewModels;

namespace Elynt.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _model;
        Loaded += async (_, _) => await _model.ConnectAsync();
    }

    private async void Retry(object sender, RoutedEventArgs e) => await _model.ConnectAsync();

    private void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
