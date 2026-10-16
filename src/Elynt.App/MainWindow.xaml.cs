using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Navigation;
using Elynt.App.ViewModels;
using Elynt.Core;
using Elynt.Core.Diagnostics;
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
        Loaded += async (_, _) =>
        {
            await _model.ConnectAsync();
            await _model.CheckForUpdatesAsync();
        };
    }

    private void RestartIntoUpdate(object sender, RoutedEventArgs e) => _model.RestartIntoUpdate();

    private async void Retry(object sender, RoutedEventArgs e) => await _model.ConnectAsync();

    private async void InstallApps(object sender, RoutedEventArgs e) => await _model.InstallTickedAsync();

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
