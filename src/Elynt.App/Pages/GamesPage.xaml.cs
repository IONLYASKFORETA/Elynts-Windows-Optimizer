using System.Windows;
using System.Windows.Controls;
using Elynt.App.ViewModels;

namespace Elynt.App.Pages;

/// <summary>
/// The Per-game profiles page's list of games. Its DataContext is the window's <see cref="MainViewModel"/>; the list
/// itself reads <see cref="MainViewModel.Profiles"/>.
/// </summary>
public partial class GamesPage : UserControl
{
    public GamesPage() => InitializeComponent();

    private MainViewModel Model => (MainViewModel)DataContext;

    /// <summary>Whether profiles raise quiet tray notes when they apply, undo or are skipped. Saved for this user.</summary>
    public bool NotifyProfiles
    {
        get => TrayNoteSettings.Current.Profiles;
        set
        {
            TrayNoteSettings.Current.Profiles = value;
            TrayNoteSettings.Current.Save();
        }
    }

    private async void AddGame(object sender, RoutedEventArgs e)
    {
        if (FilePickers.Game(Window.GetWindow(this)) is { } exe)
            await Model.AddGameAsync(exe);
    }

    private async void RemoveGame(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProfilesPageViewModel.GameRow row })
            await Model.RemoveGameAsync(row);
    }
}
