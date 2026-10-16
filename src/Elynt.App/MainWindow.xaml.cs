using System.Diagnostics;
using System.IO;
using System.Windows;
using Elynt.Core.Diagnostics;
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
            new("App log", App.LogFolder, "app-*.log"),
            new("Service log", serviceLogs, "service-*.log"),
            new("Latest repair log", Path.Combine(serviceLogs, "repair"), "repair-*.log"),
        ]);
        Clipboard.SetText(report);
        AppLogStatus.Text = "Copied. Paste it into a message to Aden.";
    }

    private void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
