using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Elynt.App.ViewModels;
using Elynt.Core;
using Elynt.Core.Startup;

namespace Elynt.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;
    private TrayNotes? _tray;

    public MainWindow()
    {
        _model = new MainViewModel(ConfirmDestructive);
        InitializeComponent();
        DataContext = _model;
        _model.Startup.ConfirmDelete = ConfirmDeleteStartupItem;
        Loaded += async (_, _) =>
        {
            await _model.ConnectAsync();
            _tray = new TrayNotes(_model.Startup, ShowFromTray);
            // The first look after connecting takes note of what is already there.
            await _tray.CheckAsync();
            await _model.CheckForUpdatesAsync();
        };
        Closed += (_, _) => _tray?.Dispose();
    }

    private void RestartIntoUpdate(object sender, RoutedEventArgs e) => _model.RestartIntoUpdate();

    private async void Retry(object sender, RoutedEventArgs e) => await _model.ConnectAsync();

    private async void InstallApps(object sender, RoutedEventArgs e) => await _model.InstallTickedAsync();

    /// <summary>The separate confirmation a destructive tweak needs. "No" is the default button.</summary>
    private bool ConfirmDestructive(TweakView tweak) =>
        MessageBox.Show(
            this,
            $"{tweak.Name}\n\nThis can't be undone. {tweak.DestructiveWarning}\n\nDo it anyway?",
            "This can't be undone",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>The separate confirmation deleting a startup item needs. "No" is the default button.</summary>
    private bool ConfirmDeleteStartupItem(StartupItemView item) =>
        MessageBox.Show(
            this,
            $"Delete {item.Name} from startup?\n\n{item.Location}\n\nThis can't be undone. Turning it off instead stops it starting and can be undone.\n\nDelete it anyway?",
            "This can't be undone",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private async void UndoChange(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HistoryItemViewModel item)
            await _model.UndoAsync(item);
    }

    private async void RestoreEverything(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "Undo every change the app made that is still in place, newest first?\n\nAnything something else has changed since is left alone and listed.",
            "Restore everything",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (answer == MessageBoxResult.OK)
            await _model.RestoreEverythingAsync();
    }

    private void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
