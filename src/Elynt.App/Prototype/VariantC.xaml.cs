using System.Windows;
using System.Windows.Controls;

namespace Elynt.App.Prototype;

/// <summary>PROTOTYPE, throwaway. See VariantC.xaml for the layout.</summary>
public partial class VariantC : UserControl
{
    public VariantC() => InitializeComponent();

    private void OpenSection(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrototypeViewModel prototype && (sender as FrameworkElement)?.Tag is string section)
            prototype.Open(section);
    }
}
