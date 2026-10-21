using System.Runtime.InteropServices;

namespace Elynt.Windows;

/// <summary>
/// What a service can see of sign-ins and sleep without a window: the user on the console session, and how long the
/// PC has slept since boot (tick count, which counts sleep, minus the unbiased interrupt time, which doesn't).
/// </summary>
public static partial class WindowsSessionWatch
{
    private const uint NoSession = 0xFFFFFFFF;
    private const int WtsUserName = 5;

    /// <summary>The user signed in on the console, or null when nobody is.</summary>
    public static string? ConsoleUser()
    {
        var session = WTSGetActiveConsoleSessionId();
        if (session == NoSession)
            return null;
        if (!WTSQuerySessionInformationW(0, session, WtsUserName, out var buffer, out _))
            return null;
        try
        {
            return Marshal.PtrToStringUni(buffer) is { Length: > 0 } user ? $"{session}:{user}" : null;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    /// <summary>Total time asleep or hibernating since boot. A jump between two reads means the PC just resumed.</summary>
    public static TimeSpan TimeAsleep()
    {
        var awake = QueryUnbiasedInterruptTime(out var unbiased) ? TimeSpan.FromTicks((long)unbiased) : TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(Environment.TickCount64) - awake;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint WTSGetActiveConsoleSessionId();

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformationW(nint server, uint sessionId, int infoClass, out nint buffer, out uint bytes);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryUnbiasedInterruptTime(out ulong time);
}
