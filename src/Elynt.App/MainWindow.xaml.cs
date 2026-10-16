using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Elynt.App.ViewModels;
using Elynt.Core;

namespace Elynt.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    public MainWindow()
    {
        _model = new MainViewModel(ConfirmDestructive);
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

    private void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
