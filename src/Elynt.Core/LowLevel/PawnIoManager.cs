using Elynt.Core.Bios;
using Elynt.Core.Games;
using Elynt.Core.Sensors;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Logging;

namespace Elynt.Core.LowLevel;

/// <summary>
/// Installs and removes PawnIO when the user asks, and reads through it while it is installed. Nothing here runs
/// PawnIO's setup except <see cref="Install"/>, which needs the FACEIT warning accepted first.
/// </summary>
internal sealed class PawnIoManager(SystemAdapters system, Func<IReadOnlyList<InstalledGame>> games, ILogger log)
{
    private const string PawnIoSource = "PawnIO";

    /// <summary>Exit codes meaning "done, restart to finish" (Windows Installer conventions).</summary>
    private static readonly int[] RebootCodes = [3010, 1641];

    private readonly PawnIoAdapters? _adapters = system.PawnIo;

    public PawnIoReport Report() => Report(null);

    public PawnIoResult Install(bool acceptFaceitWarning)
    {
        if (_adapters is not { } adapters)
            return Failed("PawnIO can't be installed from the service on this PC.");
        if (Version() is { } version)
            return Done($"PawnIO {version} is already installed.");
        if (!acceptFaceitWarning)
            return Failed($"PawnIO wasn't installed. Read the FACEIT warning and accept it first. {FaceitWarning(FaceitGames())}");

        Directory.CreateDirectory(adapters.Folder);
        var setup = Path.Combine(adapters.Folder, $"PawnIO_setup-{Guid.NewGuid():N}.exe");
        try
        {
            if (adapters.Release.SetupUrl.Scheme != Uri.UriSchemeHttps)
                return Failed($"PawnIO's setup address isn't HTTPS ({adapters.Release.SetupUrl}), so it wasn't downloaded. Nothing was installed.");
            try
            {
                log.LogInformation("Downloading PawnIO's setup from {Url}", adapters.Release.SetupUrl);
                adapters.Downloader.Download(adapters.Release.SetupUrl, setup);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Downloading PawnIO's setup failed");
                return Failed($"Couldn't download PawnIO's setup: {ex.Message} Nothing was installed.");
            }

            if (Verify(setup) is { } refusal)
            {
                log.LogWarning("Refused PawnIO's setup: {Refusal}", refusal);
                return Failed($"{refusal} The download was deleted and nothing was installed.");
            }

            int exitCode;
            try
            {
                exitCode = adapters.Driver.Install(setup);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Running PawnIO's setup failed");
                return Failed($"Couldn't run PawnIO's setup: {ex.Message}");
            }

            log.LogInformation("PawnIO's setup exited with {ExitCode}", exitCode);
            var reboot = RebootCodes.Contains(exitCode);
            if ((exitCode != 0 && !reboot) || Version() is not { } installed)
                return Failed($"PawnIO's setup failed (exit code {exitCode}). PawnIO isn't installed.");

            var modules = DownloadModules(adapters);
            var message = $"Installed PawnIO {installed}. {FaceitWarning(FaceitGames())}";
            if (modules is not null)
                message += $" {modules}";
            if (reboot)
                message += " Restart the PC to finish.";
            return new PawnIoResult(true, message, Report(null), reboot);
        }
        finally
        {
            TryDelete(setup);
        }
    }

    public PawnIoResult Uninstall()
    {
        if (_adapters is not { } adapters)
            return Failed("PawnIO can't be managed from the service on this PC.");
        if (Version() is null)
        {
            RemoveModules(adapters);
            return Done("PawnIO isn't installed. Nothing to remove.");
        }

        int exitCode;
        try
        {
            exitCode = adapters.Driver.Uninstall();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Running PawnIO's uninstaller failed");
            return Failed($"Couldn't run PawnIO's uninstaller: {ex.Message} PawnIO is still installed.");
        }

        log.LogInformation("PawnIO's uninstaller exited with {ExitCode}", exitCode);
        var reboot = RebootCodes.Contains(exitCode);
        if ((exitCode != 0 && !reboot) || Version() is not null)
            return Failed($"PawnIO's uninstaller failed (exit code {exitCode}). PawnIO is still installed.");

        RemoveModules(adapters);
        var message = "Uninstalled PawnIO. Sensors now come from your GPU's driver and Windows.";
        if (reboot)
            message += " Restart the PC to finish removing the driver.";
        return new PawnIoResult(true, message, Report(null), reboot);
    }

    /// <summary>Live values: PawnIO's first while it is installed, then each other source in order. Each sensor is shown once.</summary>
    public SensorReport Sensors()
    {
        var views = new List<SensorView>();
        var fromPawnIo = false;

        if (Version() is not null && _adapters is { } adapters)
        {
            foreach (var reading in Safely("PawnIO CPU", () => PawnIoReadings(adapters.Cpu)))
            {
                views.Add(View(reading, PawnIoSource));
                fromPawnIo = true;
            }
        }

        foreach (var source in system.Sensors)
            foreach (var reading in Safely(source.Name, source.Read))
                if (!views.Any(v => v.Id == reading.Id))
                    views.Add(View(reading, source.Name));

        string? note = null;
        if (!fromPawnIo)
            note = views.Any(v => v.Id == SensorIds.CpuTemperature)
                ? "The CPU temperature comes from Windows' ACPI thermal zone, which is often a motherboard sensor rather than the CPU's own. Turn on PawnIO to read the CPU's own sensor."
                : "Windows doesn't report the CPU's own temperature. Turn on PawnIO to read it.";
        return new SensorReport(views, fromPawnIo, note);
    }

    /// <summary>Fills in what only PawnIO can read (PBO) when the hardware readout couldn't tell.</summary>
    public HardwareSnapshot? Fill(HardwareSnapshot? hardware)
    {
        if (hardware is null || hardware.Pbo != ReadoutState.Unknown || _adapters is not { } adapters)
            return hardware;
        if (Cpu.Parse(hardware.CpuName).Maker != CpuMaker.Amd || Version() is null)
            return hardware;

        try
        {
            return adapters.Cpu.PptLimitWatts() is { } ppt ? hardware with { Pbo = PboFromPpt.State(ppt) } : hardware;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't read the PPT limit through PawnIO");
            return hardware;
        }
    }

    private static IEnumerable<SensorReading> PawnIoReadings(IPawnIoCpu cpu)
    {
        if (cpu.Temperature() is { } temperature)
            yield return new SensorReading(SensorIds.CpuTemperature, "CPU temperature", SensorKind.Temperature, temperature, "°C");
    }

    private IReadOnlyList<SensorReading> Safely(string source, Func<IEnumerable<SensorReading>> read)
    {
        try
        {
            return read().ToList();
        }
        catch (Exception ex)
        {
            // One broken source must not hide the others.
            log.LogWarning(ex, "Reading sensors from {Source} failed", source);
            return [];
        }
    }

    private static SensorView View(SensorReading r, string source) => new(r.Id, r.Name, r.Kind, r.Value, r.Unit, source);

    private string? Version()
    {
        try
        {
            return _adapters?.Driver.InstalledVersion();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't tell whether PawnIO is installed");
            return null;
        }
    }

    private string? Verify(string path)
    {
        try
        {
            var signature = _adapters!.Signatures.Check(path);
            if (!signature.Trusted)
                return "PawnIO's setup isn't validly signed, so it wasn't run.";
            log.LogInformation("PawnIO's setup is signed by {Signer}", signature.Signer);
            return null;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Checking the signature of {Path} failed", path);
            return $"Couldn't check the signature of PawnIO's setup: {ex.Message}";
        }
    }

    /// <returns>A note when a module couldn't be downloaded, or null when all were.</returns>
    private string? DownloadModules(PawnIoAdapters adapters)
    {
        Directory.CreateDirectory(adapters.ModulesFolder);
        var missing = new List<string>();
        foreach (var (name, url) in adapters.Release.Modules)
        {
            try
            {
                if (url.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException($"{url} isn't HTTPS.");
                // The driver loads only modules signed by PawnIO's author, so a tampered module can't run.
                adapters.Downloader.Download(url, Path.Combine(adapters.ModulesFolder, name));
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Downloading PawnIO module {Module} failed", name);
                missing.Add(Path.GetFileNameWithoutExtension(name));
            }
        }

        return missing.Count == 0
            ? null
            : $"Couldn't download PawnIO's {string.Join(", ", missing)} module{(missing.Count == 1 ? "" : "s")}, so some CPU sensors still come from Windows.";
    }

    private void RemoveModules(PawnIoAdapters adapters)
    {
        try
        {
            if (Directory.Exists(adapters.ModulesFolder))
                Directory.Delete(adapters.ModulesFolder, recursive: true);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't delete PawnIO's modules");
        }
    }

    private PawnIoReport Report(string? message)
    {
        var faceit = FaceitGames();
        var warning = FaceitWarning(faceit);
        if (_adapters is null)
            return new PawnIoReport(false, false, null, warning, faceit, message ?? "PawnIO can't be installed from the service on this PC.");

        var version = Version();
        message ??= version is null
            ? "PawnIO isn't installed. Sensors come from your GPU's driver and Windows. Turn it on to read the CPU's own sensors."
            : $"PawnIO {version} is installed. Uninstall the driver before playing on FACEIT.";
        return new PawnIoReport(true, version is not null, version, warning, faceit, message);
    }

    private IReadOnlyList<string> FaceitGames()
    {
        try
        {
            return games().Where(g => g.AntiCheats.Contains(AntiCheat.FaceIt)).Select(g => g.Name).Distinct().ToList();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't list installed games for the FACEIT warning");
            return [];
        }
    }

    /// <summary>
    /// FACEIT's anti-cheat refuses PawnIO because its signing certificate is shared with cheat software
    /// (research/overclocking-and-driver-tools.md, community reports). Warns; never blocks.
    /// </summary>
    internal static string FaceitWarning(IReadOnlyList<string> games)
    {
        const string blocks = "FACEIT's anti-cheat blocks PawnIO, so while it is installed FACEIT may refuse to start.";
        return games.Count == 0
            ? $"{blocks} If you play on FACEIT, uninstall the driver first."
            : $"{blocks} This affects {JoinAnd(games)} on FACEIT. Uninstall the driver before playing on FACEIT.";
    }

    private static string JoinAnd(IReadOnlyList<string> parts) =>
        parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}";

    private PawnIoResult Failed(string message) => new(false, message, Report(null));

    private PawnIoResult Done(string message) => new(true, message, Report(null));

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Couldn't delete {Path}", path);
        }
    }
}
