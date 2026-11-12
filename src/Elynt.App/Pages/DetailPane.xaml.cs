using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Elynt.App.ViewModels;

namespace Elynt.App.Pages;

/// <summary>
/// The detail pane: the selected tweak, the selected repair tool with its run, progress and log, or the selected
/// game's profile editor. Its DataContext is the window's <see cref="MainViewModel"/>.
/// </summary>
public partial class DetailPane : UserControl
{
    public DetailPane() => InitializeComponent();

    private MainViewModel Model => (MainViewModel)DataContext;

    private async void RunRepair(object sender, RoutedEventArgs e)
    {
        if (Model.SelectedRepairTool is not { } item)
            return;
        var confirmed = false;
        if (item.View.Confirmation is { } warning)
        {
            var answer = ConfirmDialog.Ask(Window.GetWindow(this), $"{warning}\n\nRun \"{item.Name}\" anyway?", item.Name,
                ConfirmButtons.YesNo, ConfirmRisk.Destructive);
            if (answer != ConfirmAnswer.Yes)
                return;
            confirmed = true;
        }
        await Model.RunRepairAsync(item, item.View.Id, confirmed);
    }

    private async void RunFollowUp(object sender, RoutedEventArgs e)
    {
        if (Model.SelectedRepairTool is not { Result.FollowUpToolId: { } followUpId } item
            || Model.FindRepairTool(followUpId) is not { } followUp)
            return;
        var answer = ConfirmDialog.Ask(Window.GetWindow(this), $"{followUp.WhatItDoes}\n\n{followUp.RestartText}", followUp.Name,
            ConfirmButtons.OkCancel, ConfirmRisk.Ordinary);
        if (answer == ConfirmAnswer.Ok)
            await Model.RunRepairAsync(item, followUpId, confirmed: false);
    }

    private void CopyRepairLog(object sender, RoutedEventArgs e)
    {
        if (Model.SelectedRepairTool?.Result?.LogText is { Length: > 0 } log)
            Clipboard.SetText(log);
    }

    /// <summary>Applies the selected per-game tweak to a game the user picks, as its row's button does.</summary>
    private async void ApplyForGame(object sender, RoutedEventArgs e)
    {
        if (Model.SelectedTweak is { } item)
            await Model.ApplyForGameAsync(item);
    }

    /// <summary>Sets the selected pick-one row's choice to the segment pressed, as its row's segments do.</summary>
    private async void Choose(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ChoiceOptionViewModel choice)
            await choice.ChooseAsync();
    }

    private void OpenLink(object sender, RequestNavigateEventArgs e) => Links.Open(e);
}
