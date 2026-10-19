using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Navigation;
using Elynt.App.ViewModels;
using Elynt.Core;
using Elynt.Core.Apps;
using Elynt.Core.Diagnostics;
using Elynt.Core.Drivers;
using Elynt.Core.Startup;

namespace Elynt.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;
    private TrayNotes? _tray;

    public MainWindow()
    {
        _model = new MainViewModel(ConfirmDestructive, AskToReboot, PickGame, ConfirmWarning);
        _model.AppsManager = new AppsManagerViewModel(_model, ConfirmNotReviewed, ConfirmUninstall);
        _model.ServicesPage = new ServicesPageViewModel(() => _model.Client, ConfirmDriverChange);
        _model.Cleanup.ConfirmDestructive = ConfirmDestructiveCleanup;
        _model.Drift = new DriftViewModel(() => _model.Client, ConfirmDestructiveReapply, _model.RefreshAsync);
        InitializeComponent();
        DataContext = _model;
        _model.Startup.ConfirmDelete = ConfirmDeleteStartupItem;
        Loaded += async (_, _) =>
        {
            await _model.ConnectAsync();
            _tray = new TrayNotes(_model.Startup, ShowFromTray);
            // The first look after connecting takes note of what is already there.
            await _tray.CheckAsync();
            // Once per sign-in: the window starts with Windows, so this is the per-boot drift check.
            if (await _model.Drift!.CheckAsync() is { } drift)
                _tray.ShowDrift(drift, ShowDrift);
            await _model.CheckForUpdatesAsync();
        };
        Closed += (_, _) => _tray?.Dispose();
    }

    private void RestartIntoUpdate(object sender, RoutedEventArgs e) => _model.RestartIntoUpdate();

    private async void Retry(object sender, RoutedEventArgs e) => await _model.ConnectAsync();

    private async void InstallApps(object sender, RoutedEventArgs e) => await _model.InstallTickedAsync();

    private AppsManagerViewModel Apps => _model.AppsManager!;

    private async void SearchApps(object sender, RoutedEventArgs e) => await Apps.SearchAsync();

    private async void SearchBoxKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
            return;
        e.Handled = true;
        await Apps.SearchAsync();
    }

    private async void InstallSearchResults(object sender, RoutedEventArgs e) => await Apps.InstallTickedResultsAsync();

    private async void RefreshInstalledApps(object sender, RoutedEventArgs e) => await Apps.RefreshInstalledAsync();

    private async void UpdateAllApps(object sender, RoutedEventArgs e) => await Apps.UpdateAllAsync();

    private async void UpdateApp(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is InstalledAppViewModel app)
            await Apps.UpdateAsync(app);
    }

    private async void UninstallApp(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is InstalledAppViewModel app)
            await Apps.UninstallAsync(app);
    }

    private bool ConfirmNotReviewed(IReadOnlyList<string> names) =>
        MessageBox.Show(
            this,
            $"These apps aren't in the reviewed catalogue:\n\n{string.Join("\n", names)}\n\n" +
            "Nobody has checked that they are free, safe or install silently. Install them only if you trust the publisher.\n\nInstall anyway?",
            "Not reviewed",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    private bool ConfirmUninstall(string name, string note) =>
        MessageBox.Show(this, $"Uninstall {name}?\n\n{note}".TrimEnd(), "Uninstall",
            MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) == MessageBoxResult.OK;

    private void SaveAppList(object sender, RoutedEventArgs e)
    {
        var (text, count) = Apps.BuildAppList();
        if (count == 0)
        {
            MessageBox.Show(this, "Tick the apps you want in the list first, on the Catalogue or Search tab.", "Save app list");
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "My apps" + AppList.FileExtension,
            Filter = $"Elynt app list (*{AppList.FileExtension})|*{AppList.FileExtension}",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        File.WriteAllText(dialog.FileName, text);
        MessageBox.Show(this, $"Saved {count} {(count == 1 ? "app" : "apps")}. Load the file with \"Load app list\" on another PC to install them there.", "Save app list");
    }

    private void LoadAppList(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = $"Elynt app list (*{AppList.FileExtension})|*{AppList.FileExtension}|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        string text;
        try
        {
            text = File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Couldn't read the file: {ex.Message}", "Load app list");
            return;
        }
        MessageBox.Show(this, Apps.LoadAppList(text), "Load app list");
    }

    private async void RunRepair(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedRepairTool is not { } item)
            return;
        var confirmed = false;
        if (item.View.Confirmation is { } warning)
        {
            var answer = MessageBox.Show(this, $"{warning}\n\nRun \"{item.Name}\" anyway?", item.Name,
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;
            confirmed = true;
        }
        await _model.RunRepairAsync(item, item.View.Id, confirmed);
    }

    private async void RunFollowUp(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedRepairTool is not { Result.FollowUpToolId: { } followUpId } item
            || _model.FindRepairTool(followUpId) is not { } followUp)
            return;
        var answer = MessageBox.Show(this, $"{followUp.WhatItDoes}\n\n{followUp.RestartText}", followUp.Name,
            MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.OK)
            await _model.RunRepairAsync(item, followUpId, confirmed: false);
    }

    private void CopyRepairLog(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedRepairTool?.Result?.LogText is { Length: > 0 } log)
            Clipboard.SetText(log);
    }

    /// <summary>Copies the newest app, service and repair logs, so a friend can paste them into a bug report.</summary>
    private void CopyAppLog(object sender, RoutedEventArgs e)
    {
        var serviceLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ElyntOptimizer", "logs");
        var report = BugReport.Build(
        [
            new("App log", AppLog.Folder, "app-*.log"),
            new("Service log", serviceLogs, "service-*.log"),
            new("Latest repair log", Path.Combine(serviceLogs, "repair"), "repair-*.log"),
        ]);
        Clipboard.SetText(report);
        AppLogStatus.Text = "Copied. Paste it into a message to Aden.";
    }

    /// <summary>The separate confirmation a destructive tweak needs. "No" is the default button.</summary>
    private bool ConfirmDestructive(TweakView tweak) =>
        MessageBox.Show(
            this,
            $"{tweak.Name}\n\nThis can't be undone. {tweak.DestructiveWarning}\n\nDo it anyway?",
            "This can't be undone",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>The warning a tweak needs read before it is applied (OneDrive, the Store). "No" is the default button.</summary>
    private bool ConfirmWarning(TweakView tweak) =>
        MessageBox.Show(
            this,
            $"{tweak.Name}\n\n{tweak.ApplyWarning}\n\nGo ahead?",
            "Before you go on",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>Drivers can stop Windows from starting, so changing one is asked about first. "No" is the default.</summary>
    private bool ConfirmDriverChange(ServiceItemViewModel item, Elynt.Core.Tweaks.ServiceStart start) =>
        MessageBox.Show(
            this,
            $"{item.Title} is a driver. Setting it to {start} can stop Windows from starting if it is needed for your hardware.\n\n" +
            "The previous configuration is saved and can be put back from the Services page or the Undo history.\n\nChange it anyway?",
            "Change a driver",
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

    private async void ScanCleanup(object sender, RoutedEventArgs e) => await _model.Cleanup.ScanAsync();

    private async void CleanTicked(object sender, RoutedEventArgs e) => await _model.Cleanup.CleanAsync();

    /// <summary>The separate confirmation destructive cleanup categories need. "No" is the default button.</summary>
    private bool ConfirmDestructiveCleanup(string warnings) =>
        MessageBox.Show(
            this,
            $"These can't be undone:\n\n{warnings}\n\nClean them anyway?",
            "This can't be undone",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    private async void SetServiceStart(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ServiceItemViewModel item && _model.ServicesPage is { } page)
            await page.SetAsync(item);
    }

    private async void UndoServiceChange(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ServiceItemViewModel item && _model.ServicesPage is { } page)
            await page.UndoAsync(item);
    }

    private void ShowDrift()
    {
        ShowFromTray();
        _model.SelectedPage = MainViewModel.UndoHistory;
    }

    private async void ReapplyAllDrift(object sender, RoutedEventArgs e) => await _model.Drift!.ReapplyAllAsync();

    private async void ReapplyTickedDrift(object sender, RoutedEventArgs e) => await _model.Drift!.ReapplyTickedAsync();

    private bool ConfirmDestructiveReapply(string names) =>
        MessageBox.Show(
            this,
            $"Windows undid these, and putting them back can't be undone:\n\n{names}\n\nRe-apply them too? Choose No to re-apply only the rest.",
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

    private string? PickGame()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose the game's .exe",
            Filter = "Programs (*.exe)|*.exe",
            CheckFileExists = true,
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private async void AddGame(object sender, RoutedEventArgs e)
    {
        if (PickGame() is { } exe)
            await _model.AddGameAsync(exe);
    }

    private async void RemoveGame(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GamesPageViewModel.GameItem item })
            await _model.RemoveGameAsync(item);
    }

    /// <summary>Applies a per-game tweak to a game the user picks.</summary>
    private async void ApplyForGame(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TweakItemViewModel item)
            await _model.ApplyForGameAsync(item);
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

    private async void CheckAndInstallWindowsUpdates(object sender, RoutedEventArgs e) =>
        await _model.WindowsUpdate.StartAsync();

    private void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
