using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
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

    /// <summary>
    /// Brings a row selected from elsewhere (the sidebar's search) into view, opening the possible-placebo fold when
    /// the row is in it.
    /// </summary>
    private void ShowSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list || list.SelectedItem is not TweakItemViewModel row)
            return;
        if (row.IsPlacebo)
        {
            foreach (var fold in Descendants<Expander>(list))
                fold.IsExpanded = true;
        }
        // Once the page (and the fold) have been laid out.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => list.ScrollIntoView(row));
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }
}
