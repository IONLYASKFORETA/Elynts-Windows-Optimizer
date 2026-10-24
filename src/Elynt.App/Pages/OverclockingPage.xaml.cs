using System.Windows.Controls;
using System.Windows.Navigation;

namespace Elynt.App.Pages;

/// <summary>The Overclocking page: GPU and CPU tuning, sensors and BIOS guides. Its DataContext is the <see cref="ViewModels.MainViewModel"/>.</summary>
public partial class OverclockingPage : UserControl
{
    public OverclockingPage() => InitializeComponent();

    private void OpenLink(object sender, RequestNavigateEventArgs e) => Links.Open(e);
}
