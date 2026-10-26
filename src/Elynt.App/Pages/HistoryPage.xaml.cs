using System.Windows;
using System.Windows.Controls;
using Elynt.App.ViewModels;

namespace Elynt.App.Pages;

/// <summary>The Undo history, with drift above it. Its DataContext is the window's <see cref="MainViewModel"/>.</summary>
public partial class HistoryPage : UserControl
{
    public HistoryPage() => InitializeComponent();

    private MainViewModel Model => (MainViewModel)DataContext;

    private async void ReapplyAllDrift(object sender, RoutedEventArgs e) => await Model.Drift!.ReapplyAllAsync();

    private async void ReapplyTickedDrift(object sender, RoutedEventArgs e) => await Model.Drift!.ReapplyTickedAsync();

    private async void UndoChange(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HistoryItemViewModel item)
            await Model.UndoAsync(item);
    }

    private async void RestoreEverything(object sender, RoutedEventArgs e)
    {
        var answer = ConfirmDialog.Ask(
            Window.GetWindow(this),
            "Undo every change the app made that is still in place, newest first?\n\nAnything something else has changed since is left alone and listed. Changes that can't be undone are left as they are.",
            "Restore everything",
            ConfirmButtons.OkCancel,
            ConfirmRisk.Destructive);
        if (answer == ConfirmAnswer.Ok)
            await Model.RestoreEverythingAsync();
    }
}
