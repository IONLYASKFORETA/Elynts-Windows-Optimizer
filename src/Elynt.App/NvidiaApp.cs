using System.Diagnostics;
using System.IO;
using Serilog;

namespace Elynt.App;

/// <summary>
/// Opens the NVIDIA App, where NVIDIA's Automatic Tuning lives (System → Performance → Automatic Tuning). It has no
/// API, so the app can only open it; when it isn't installed, NVIDIA's download page opens instead.
/// </summary>
internal static class NvidiaApp
{
    private const string DownloadPage = "https://www.nvidia.com/en-us/software/nvidia-app/";

    public static void Open()
    {
        try
        {
            // The NVIDIA App's usual install location (not verified on every version).
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation", "NVIDIA App", "CEF", "NVIDIA App.exe");
            Process.Start(new ProcessStartInfo(File.Exists(exe) ? exe : DownloadPage) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't open the NVIDIA App");
        }
    }
}
