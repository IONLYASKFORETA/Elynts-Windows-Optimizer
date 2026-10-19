using Elynt.Core.LowLevel;

namespace Elynt.Core.Tests;

/// <summary>An in-memory PawnIO: its setup "installs" the driver, its uninstaller removes it.</summary>
public sealed class FakePawnIo : IPawnIoDriver, IPawnIoCpu
{
    public string? Version { get; set; }

    /// <summary>The version the setup installs.</summary>
    public string SetupVersion { get; set; } = "2.0.1";

    public int InstallExitCode { get; set; }
    public int UninstallExitCode { get; set; }

    /// <summary>Installs that ran, with the bytes of the setup they ran.</summary>
    public List<byte[]> Installs { get; } = [];

    public int Uninstalls { get; private set; }

    public double? CpuTemperature { get; set; }
    public double? PptLimit { get; set; }

    /// <summary>Reads through the driver, counted to show nothing reads through PawnIO while it isn't installed.</summary>
    public int CpuReads { get; private set; }

    public string? InstalledVersion() => Version;

    public int Install(string setupPath)
    {
        Installs.Add(File.ReadAllBytes(setupPath));
        if (InstallExitCode is 0 or 3010)
            Version = SetupVersion;
        return InstallExitCode;
    }

    public int Uninstall()
    {
        Uninstalls++;
        if (UninstallExitCode is 0 or 3010)
            Version = null;
        return UninstallExitCode;
    }

    public double? Temperature()
    {
        CpuReads++;
        return CpuTemperature;
    }

    public double? PptLimitWatts()
    {
        CpuReads++;
        return PptLimit;
    }
}
