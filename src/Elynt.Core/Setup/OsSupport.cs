namespace Elynt.Core.Setup;

/// <summary>The facts about the running Windows that decide whether the app may run at all.</summary>
/// <param name="Build">The Windows build number, e.g. 26100.</param>
/// <param name="IsServer">True on Windows Server (and domain controllers), false on desktop Windows.</param>
public sealed record OsDescription(int Build, bool IsServer);

public sealed record OsSupportResult(bool Supported, string Message);

/// <summary>
/// The app is for Windows 11 only: its tweaks are written and tested for Windows 11, and some would do the wrong
/// thing on another OS. On anything else the app explains this and exits without changing anything.
/// </summary>
public static class OsSupport
{
    /// <summary>Windows 11 shares the "10.0" version number with Windows 10; the build number tells them apart.</summary>
    public const int FirstWindows11Build = 22000;

    public static OsSupportResult Check(OsDescription os)
    {
        if (os.IsServer)
            return new(false,
                "Elynt's Windows Optimizer only runs on Windows 11. This PC runs Windows Server, so the app will " +
                "close without changing anything.");

        if (os.Build < FirstWindows11Build)
            return new(false,
                $"Elynt's Windows Optimizer only runs on Windows 11. This PC runs an older Windows (build {os.Build}), " +
                "so the app will close without changing anything.");

        return new(true, "");
    }
}
