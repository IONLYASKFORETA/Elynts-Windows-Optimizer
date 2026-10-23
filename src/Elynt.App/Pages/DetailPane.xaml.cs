using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Elynt.App.ViewModels;

namespace Elynt.App.Pages;

/// <summary>
/// The detail pane: the selected tweak, or the selected repair tool with its run, progress and log. Its DataContext
/// is the window's <see cref="MainViewModel"/>.
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

    private void OpenLink(object sender, RequestNavigateEventArgs e) => Links.Open(e);
}
