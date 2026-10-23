using System.Windows;
using System.Windows.Controls;
using Elynt.App.ViewModels;

namespace Elynt.App.Pages;

/// <summary>A section's tweaks, one switch each. Its DataContext is the window's <see cref="MainViewModel"/>.</summary>
public partial class TweakList : UserControl
{
    public TweakList() => InitializeComponent();

    private MainViewModel Model => (MainViewModel)DataContext;

    /// <summary>Applies a per-game tweak to a game the user picks.</summary>
    private async void ApplyForGame(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TweakItemViewModel item)
            await Model.ApplyForGameAsync(item);
    }
}
