using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Elynt.App.ViewModels;
using Elynt.Core.Apps;

namespace Elynt.App.Pages;

/// <summary>The Apps page: catalogue, winget search and installed apps. Its DataContext is the <see cref="MainViewModel"/>.</summary>
public partial class AppsPage : UserControl
{
    public AppsPage() => InitializeComponent();

    private MainViewModel Model => (MainViewModel)DataContext;

    private AppsManagerViewModel Apps => Model.AppsManager!;

    private async void InstallApps(object sender, RoutedEventArgs e) => await Model.InstallTickedAsync();

    private async void SearchApps(object sender, RoutedEventArgs e) => await Apps.SearchAsync();

    private async void SearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
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

    private void SaveAppList(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var (text, count) = Apps.BuildAppList();
        if (count == 0)
        {
            ConfirmDialog.Tell(owner, "Tick the apps you want in the list first, on the Catalogue or Search tab.", "Save app list");
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "My apps" + AppList.FileExtension,
            Filter = $"Elynt app list (*{AppList.FileExtension})|*{AppList.FileExtension}",
        };
        if (dialog.ShowDialog(owner) != true)
            return;
        File.WriteAllText(dialog.FileName, text);
        ConfirmDialog.Tell(owner, $"Saved {count} {(count == 1 ? "app" : "apps")}. Load the file with \"Load app list\" on another PC to install them there.", "Save app list");
    }

    private void LoadAppList(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = $"Elynt app list (*{AppList.FileExtension})|*{AppList.FileExtension}|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(owner) != true)
            return;
        string text;
        try
        {
            text = File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ConfirmDialog.Tell(owner, $"Couldn't read the file: {ex.Message}", "Load app list");
            return;
        }
        ConfirmDialog.Tell(owner, Apps.LoadAppList(text), "Load app list");
    }
}
