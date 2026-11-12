using System.Windows;
using System.Windows.Controls;
using Elynt.App.ViewModels;

namespace Elynt.App.Pages;

/// <summary>The selected game's profile editor, in the detail pane. Its DataContext is the <see cref="ProfilesPageViewModel"/>.</summary>
public partial class ProfileEditor : UserControl
{
    public ProfileEditor() => InitializeComponent();

    private ProfilesPageViewModel? Model => DataContext as ProfilesPageViewModel;

    private void ChooseExe(object sender, RoutedEventArgs e) => Model?.ChooseExe();

    /// <summary>Sets a pick-one choice (the power plan) to the segment pressed. Saved with the profile.</summary>
    private void Choose(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProfileChoiceOption option)
            option.Choose();
    }

    private void AddApp(object sender, RoutedEventArgs e) => Model?.AddApp();

    /// <summary>The "×" on an app's row: takes that app off the list.</summary>
    private void RemoveApp(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AppToClose app)
            Model?.RemoveApp(app);
    }

    private async void UseCurrentGpu(object sender, RoutedEventArgs e)
    {
        if (Model is { } model)
            await model.UseCurrentGpuAsync();
    }

    private async void UseCurrentCpu(object sender, RoutedEventArgs e)
    {
        if (Model is { } model)
            await model.UseCurrentCpuAsync();
    }

    private void ClearGpu(object sender, RoutedEventArgs e) => Model?.ClearGpu();

    private void ClearCpu(object sender, RoutedEventArgs e) => Model?.ClearCpu();

    private async void Save(object sender, RoutedEventArgs e)
    {
        if (Model is { } model)
            await model.SaveAsync();
    }

    private async void Delete(object sender, RoutedEventArgs e)
    {
        if (Model is { } model)
            await model.DeleteAsync();
    }
}
