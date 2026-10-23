using System.Windows;
using System.Windows.Controls;
using Elynt.App.ViewModels;

namespace Elynt.App.Pages;

public partial class SensorsPanel : UserControl
{
    public SensorsPanel()
    {
        InitializeComponent();
    }

    private SensorsPageViewModel? Model => DataContext as SensorsPageViewModel;

    /// <summary>PawnIO is installed only after the user accepts the FACEIT warning here.</summary>
    private async void TurnOn(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model)
            return;
        var accepted = MessageBox.Show(
            Window.GetWindow(this)!,
            $"Install the PawnIO driver?\n\n{model.FaceitWarning}\n\nYou can remove it any time with \"Uninstall driver\".",
            "Turn on PawnIO",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
        if (accepted)
            await model.InstallAsync();
    }

    private async void Uninstall(object sender, RoutedEventArgs e)
    {
        if (Model is { } model)
            await model.UninstallAsync();
    }
}
