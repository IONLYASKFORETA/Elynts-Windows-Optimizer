using System.Windows;
using System.Windows.Controls;
using Elynt.App.ViewModels;

namespace Elynt.App;

/// <summary>The per-game profile editor. Its DataContext is the <see cref="ProfilesPageViewModel"/>.</summary>
public partial class ProfilesPage : UserControl
{
    public ProfilesPage() => InitializeComponent();

    private ProfilesPageViewModel? Model => DataContext as ProfilesPageViewModel;

    private void AddApp(object sender, RoutedEventArgs e) => Model?.AddApp();

    private void RemoveApp(object sender, RoutedEventArgs e) => Model?.RemoveApp();

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
