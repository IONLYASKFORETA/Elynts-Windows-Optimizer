using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Elynt.App.Theme;

/// <summary>Colours Windows' own title bar and window edge to match the app (Windows 11; ignored elsewhere).</summary>
internal static partial class TitleBar
{
    private const int BorderColour = 34; // DWMWA_BORDER_COLOR
    private const int CaptionColour = 35; // DWMWA_CAPTION_COLOR
    private const int TextColour = 36; // DWMWA_TEXT_COLOR

    public static void Paint(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        Set(handle, CaptionColour, 0x09, 0x09, 0x0B);
        Set(handle, BorderColour, 0x26, 0x26, 0x2D);
        Set(handle, TextColour, 0xF4, 0xF4, 0xF5);
    }

    private static void Set(nint window, int attribute, byte red, byte green, byte blue)
    {
        var colour = red | green << 8 | blue << 16; // COLORREF: 0x00BBGGRR
        _ = DwmSetWindowAttribute(window, attribute, ref colour, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
