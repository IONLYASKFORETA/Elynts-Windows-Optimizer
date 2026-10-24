using System.Windows;
using System.Windows.Controls;
using Elynt.App.ViewModels;

namespace Elynt.App.Pages;

/// <summary>Per-game profiles, then the installed games. Its DataContext is the window's <see cref="MainViewModel"/>.</summary>
public partial class GamesPage : UserControl
{
    public GamesPage() => InitializeComponent();

    private MainViewModel Model => (MainViewModel)DataContext;

    private async void AddGame(object sender, RoutedEventArgs e)
    {
        if (FilePickers.Game(Window.GetWindow(this)) is { } exe)
            await Model.AddGameAsync(exe);
    }

    private async void RemoveGame(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GamesPageViewModel.GameItem item })
            await Model.RemoveGameAsync(item);
    }
}
