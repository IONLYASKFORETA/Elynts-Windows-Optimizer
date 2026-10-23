using System.Diagnostics;
using System.Windows.Navigation;

namespace Elynt.App;

/// <summary>Opens a page's links (sources, vendor pages, guides) in the default browser.</summary>
internal static class Links
{
    public static void Open(RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
