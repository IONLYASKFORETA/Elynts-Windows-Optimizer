using System.Diagnostics.Eventing.Reader;
using Elynt.Core.Tuning.Cpu;

namespace Elynt.Windows;

/// <summary>
/// Crashes and hardware errors from the Windows System event log: Kernel-Power 41 ("rebooted without cleanly shutting
/// down"), EventLog 6008 ("the previous system shutdown was unexpected") and any WHEA-Logger event.
/// Not yet verified against a real crash on this app's code path.
/// </summary>
public sealed class WindowsCpuStabilityEvents : ICpuStabilityEvents
{
    public bool UncleanShutdownSince(DateTimeOffset since) =>
        Count($"*[System[((Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41) or (Provider[@Name='EventLog'] and EventID=6008)) and TimeCreated[@SystemTime>='{Time(since)}']]]") > 0;

    public int WheaErrorsSince(DateTimeOffset since) =>
        Count($"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[@SystemTime>='{Time(since)}']]]");

    private static int Count(string xpath)
    {
        using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath));
        var count = 0;
        while (reader.ReadEvent() is { } record)
        {
            record.Dispose();
            count++;
        }
        return count;
    }

    private static string Time(DateTimeOffset when) =>
        (when == DateTimeOffset.MinValue ? new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero) : when)
            .UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
}
