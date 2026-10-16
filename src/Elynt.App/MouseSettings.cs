using System.Runtime.InteropServices;
using Microsoft.Win32;
using Serilog;

namespace Elynt.App;

/// <summary>
/// The service writes the user's mouse values to the registry, but Windows only reads them at sign-in.
/// This tells the signed-in session to use what is now in the registry, so the change applies immediately.
/// It changes nothing the service didn't already change.
/// </summary>
internal static partial class MouseSettings
{
    private const uint SpiSetMouse = 0x0004;
    private const uint SpifSendChange = 0x0002;

    public static void ReloadFromRegistry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");

        // SPI_SETMOUSE takes { threshold1, threshold2, acceleration }. Missing values fall back to Windows' defaults.
        int[] values =
        [
            Read(key, "MouseThreshold1", 6),
            Read(key, "MouseThreshold2", 10),
            Read(key, "MouseSpeed", 1),
        ];
        if (!SystemParametersInfo(SpiSetMouse, 0, values, SpifSendChange))
            Log.Warning("Couldn't reload mouse settings (error {Error}); they apply at next sign-in", Marshal.GetLastPInvokeError());
    }

    private static int Read(RegistryKey? key, string name, int fallback) =>
        int.TryParse(key?.GetValue(name)?.ToString(), out var value) ? value : fallback;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, int[] values, uint flags);
}
