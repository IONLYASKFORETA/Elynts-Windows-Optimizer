using System.Globalization;
using System.Windows.Data;
using Elynt.App.ViewModels;

namespace Elynt.App.Theme;

/// <summary>Groups the sidebar's pages under their headers: a page's name to its group's header ("PLAY").</summary>
public sealed class SidebarGroupHeader : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Sidebar.HeaderOf(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
