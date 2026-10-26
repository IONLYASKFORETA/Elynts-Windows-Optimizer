using System.Globalization;
using System.Windows.Data;
using Elynt.App.ViewModels;
using Elynt.Core.Tweaks;

namespace Elynt.App.Theme;

/// <summary>The sidebar icon for a page: a Segoe Fluent Icons code point (shared with Segoe MDL2 Assets).</summary>
public sealed class PageIcon : IValueConverter
{
    public static string For(string? page) => page switch
    {
        MainViewModel.Home => "",
        Sections.Gaming => "",
        Sections.DebloatAndPrivacy => "",
        Sections.Security => "",
        Sections.Apps => "",
        Sections.WindowsUpdate => "",
        Sections.StartupAndServices => "",
        Sections.Cleanup => "",
        Sections.Repair => "",
        Sections.Drivers => "",
        Sections.Overclocking => "",
        MainViewModel.PerGameProfiles => "",
        MainViewModel.UndoHistory => "",
        MainViewModel.Settings => "",
        _ => "",
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => For(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
