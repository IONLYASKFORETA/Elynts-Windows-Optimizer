using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Elynt.Core.Diagnostics;

namespace Elynt.App.Pages;

/// <summary>The Repair page's tool list. Its DataContext is the window's <see cref="ViewModels.MainViewModel"/>.</summary>
public partial class RepairPage : UserControl
{
    public RepairPage() => InitializeComponent();

    /// <summary>Brings a tool selected from elsewhere (the sidebar's search) into view, once the page is laid out.</summary>
    private void ShowSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: { } tool } list)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => list.ScrollIntoView(tool));
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
}
