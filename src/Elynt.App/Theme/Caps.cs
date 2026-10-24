using System.Globalization;
using System.Windows.Data;

namespace Elynt.App.Theme;

/// <summary>Text in capitals, for a <c>Text.Label</c> heading whose words come from data (a list's group names).</summary>
public sealed class Caps : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString()?.ToUpper(culture) ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
