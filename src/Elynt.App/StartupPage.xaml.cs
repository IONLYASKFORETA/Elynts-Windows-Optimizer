using System.Windows;
using System.Windows.Controls;
using Elynt.App.ViewModels;

namespace Elynt.App;

/// <summary>The Startup list. Its DataContext is the <see cref="StartupPageViewModel"/>.</summary>
public partial class StartupPage : UserControl
{
    public StartupPage() => InitializeComponent();

    /// <summary>Whether a new startup item raises a tray note. Saved for this user.</summary>
    public bool NotifyNewStartupItems
    {
        get => TrayNoteSettings.Current.NewStartupItems;
        set
        {
            TrayNoteSettings.Current.NewStartupItems = value;
            TrayNoteSettings.Current.Save();
        }
    }

    private StartupPageViewModel? Model => DataContext as StartupPageViewModel;

    private async void Refresh(object sender, RoutedEventArgs e)
    {
        if (Model is { } model)
            await model.LoadAsync();
    }

    private async void Delete(object sender, RoutedEventArgs e)
    {
        if (Model is { } model && (sender as FrameworkElement)?.DataContext is StartupItemViewModel item)
            await model.DeleteAsync(item);
    }
}
