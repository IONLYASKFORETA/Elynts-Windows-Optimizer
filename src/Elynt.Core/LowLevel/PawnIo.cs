using Elynt.Core.Drivers;

namespace Elynt.Core.LowLevel;

/// <summary>
/// PawnIO, the optional signed kernel driver (https://pawnio.eu/) that lets the service read the CPU's own sensors
/// and, later, tune it. The app never installs it on its own: only <see cref="IEngine.InstallPawnIo"/> does, after
/// the user has seen the FACEIT warning.
/// </summary>
public interface IPawnIoDriver
{
    /// <summary>The installed PawnIO version, or null when it isn't installed.</summary>
    string? InstalledVersion();

    /// <summary>Runs PawnIO's own setup silently and waits for it.</summary>
    /// <returns>The setup's exit code.</returns>
    int Install(string setupPath);

    /// <summary>Runs PawnIO's own uninstaller silently and waits for it.</summary>
    /// <returns>The uninstaller's exit code.</returns>
    int Uninstall();
}

/// <summary>
/// Reads the CPU through PawnIO's official signed modules. Only called while PawnIO is installed. Each read returns
/// null when this CPU or module can't give the value; it never throws for a missing value.
/// </summary>
public interface IPawnIoCpu
{
    /// <summary>The CPU's own temperature sensor (Intel package temperature, AMD Tctl), in °C.</summary>
    double? Temperature();

    /// <summary>On AMD Ryzen, the PPT (socket power) limit the SMU is enforcing right now, in watts.</summary>
    double? PptLimitWatts();
}

/// <summary>Where PawnIO's installer and modules come from.</summary>
/// <param name="SetupUrl">PawnIO's own signed setup, from its GitHub releases.</param>
/// <param name="Modules">PawnIO's official signed modules, by file name. The driver checks their signatures itself.</param>
public sealed record PawnIoRelease(Uri SetupUrl, IReadOnlyDictionary<string, Uri> Modules)
{
    /// <summary>
    /// The latest PawnIO.Setup and PawnIO.Modules releases on GitHub. Asset names not yet checked against a real
    /// release (no network from the build machine).
    /// </summary>
    public static PawnIoRelease Default { get; } = new(
        new Uri("https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe"),
        new Dictionary<string, Uri>
        {
            ["IntelMSR.bin"] = new("https://github.com/namazso/PawnIO.Modules/releases/latest/download/IntelMSR.bin"),
            ["AMDFamily17.bin"] = new("https://github.com/namazso/PawnIO.Modules/releases/latest/download/AMDFamily17.bin"),
            ["RyzenSMU.bin"] = new("https://github.com/namazso/PawnIO.Modules/releases/latest/download/RyzenSMU.bin"),
        });
}

/// <summary>Everything the PawnIO flow may touch.</summary>
/// <param name="Folder">The service's own PawnIO folder: the setup is downloaded here and the modules are kept here.</param>
public sealed record PawnIoAdapters(
    IPawnIoDriver Driver,
    IPawnIoCpu Cpu,
    string Folder,
    IDownloader Downloader,
    ISignatureVerifier Signatures,
    PawnIoRelease? Release = null)
{
    public PawnIoRelease Release { get; init; } = Release ?? PawnIoRelease.Default;

    /// <summary>Where the downloaded modules are kept; removed with the driver.</summary>
    public string ModulesFolder => Path.Combine(Folder, "modules");
}

/// <summary>PawnIO as the Overclocking page shows it.</summary>
/// <param name="Available">The service can install PawnIO on this PC.</param>
/// <param name="FaceitWarning">Shown before installing, and while it is installed. Names installed FACEIT games.</param>
/// <param name="FaceitGames">Installed games that use FACEIT's anti-cheat.</param>
public sealed record PawnIoReport(
    bool Available,
    bool Installed,
    string? Version,
    string FaceitWarning,
    IReadOnlyList<string> FaceitGames,
    string Message);

/// <param name="RebootNeeded">PawnIO's setup or uninstaller asked for a restart to finish.</param>
public sealed record PawnIoResult(bool Succeeded, string Message, PawnIoReport Report, bool RebootNeeded = false);
