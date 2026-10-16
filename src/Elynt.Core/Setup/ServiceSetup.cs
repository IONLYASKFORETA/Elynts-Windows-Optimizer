namespace Elynt.Core.Setup;

/// <summary>A registered Windows service as the Service Control Manager reports it.</summary>
public sealed record InstalledService(string BinaryPath, bool Running);

/// <summary>How the service is registered: starts automatically and restarts itself if it crashes.</summary>
public sealed record ServiceRegistration(string Name, string DisplayName, string Description, string BinaryPath);

/// <summary>The Service Control Manager. Every call except <see cref="Query"/> needs admin rights.</summary>
public interface IServiceControl
{
    /// <summary>The service, or null when it isn't registered.</summary>
    InstalledService? Query(string name);

    /// <summary>Registers a new service that starts automatically and restarts on failure.</summary>
    void Create(ServiceRegistration registration);

    /// <summary>Rewrites an existing service's registration to match.</summary>
    void Configure(ServiceRegistration registration);

    void Start(string name);

    /// <summary>Stops the service and waits until it has stopped. Does nothing if it isn't running.</summary>
    void Stop(string name);

    void Delete(string name);
}

/// <summary>The folders a service build lives in.</summary>
public interface IServiceFiles
{
    /// <summary>The version of the service executable in the folder, or null when there is none.</summary>
    string? VersionAt(string folder);

    /// <summary>Makes <paramref name="to"/> an exact copy of <paramref name="from"/>.</summary>
    void Mirror(string from, string to);

    void DeleteFolder(string folder);
}

/// <param name="BundledFolder">The service build shipped inside the app's own (per-user) install.</param>
/// <param name="InstalledFolder">
/// Where the service runs from. It runs as SYSTEM, so this must be a folder only admins can write
/// (Program Files), never the user-writable app folder.
/// </param>
public sealed record ServiceLayout(string BundledFolder, string InstalledFolder);

public enum ServiceSetupState
{
    /// <summary>Installed, running and the same version as the app.</summary>
    Ready,
    NotInstalled,
    /// <summary>Installed, but a different version from the one this app ships, or registered at another path.</summary>
    Outdated,
    /// <summary>Installed and current, but not running.</summary>
    Stopped,
    /// <summary>This copy of the app carries no service build (a developer build), so it can't manage the service.</summary>
    NoBundledService,
}

public sealed record ServiceSetupResult(bool Succeeded, string Message);

/// <summary>
/// Installs, updates, restarts and removes the app's elevated background service. Velopack installs the app per
/// user and knows nothing about services, so the app does this itself, from one elevated helper run.
/// </summary>
public sealed class ServiceSetup(IServiceControl services, IServiceFiles files, ServiceLayout layout)
{
    public const string ServiceName = "ElyntOptimizer";
    public const string ExeName = "Elynt.Service.exe";

    private string InstalledExe => Path.Combine(layout.InstalledFolder, ExeName);

    private ServiceRegistration Registration => new(
        ServiceName,
        "Elynt Optimizer",
        "Applies and undoes the changes you choose in Elynt's Windows Optimizer.",
        InstalledExe);

    public ServiceSetupState Check()
    {
        var bundled = files.VersionAt(layout.BundledFolder);
        if (bundled is null)
            return ServiceSetupState.NoBundledService;

        var service = services.Query(ServiceName);
        if (service is null)
            return ServiceSetupState.NotInstalled;

        if (!SamePath(service.BinaryPath, InstalledExe) || files.VersionAt(layout.InstalledFolder) != bundled)
            return ServiceSetupState.Outdated;

        return service.Running ? ServiceSetupState.Ready : ServiceSetupState.Stopped;
    }

    /// <summary>Brings the service to <see cref="ServiceSetupState.Ready"/>, whatever state it is in.</summary>
    public ServiceSetupResult InstallOrUpdate()
    {
        var state = Check();
        if (state == ServiceSetupState.NoBundledService)
            return new(false, "This copy of the app doesn't include the background service.");

        if (state == ServiceSetupState.Ready)
            return new(true, "The background service is already up to date and running.");

        try
        {
            if (state == ServiceSetupState.Stopped)
            {
                services.Start(ServiceName);
                return new(true, "The background service was started again.");
            }

            // A running service holds its files open, so it has to stop before they can be replaced.
            if (state == ServiceSetupState.Outdated)
                services.Stop(ServiceName);

            try
            {
                files.Mirror(layout.BundledFolder, layout.InstalledFolder);
            }
            catch (Exception ex) when (state == ServiceSetupState.Outdated)
            {
                // Don't leave the PC without its service: bring the old one back before reporting.
                TryStart();
                return new(false, $"Couldn't update the background service, so the previous version keeps running. {ex.Message}");
            }

            if (state == ServiceSetupState.NotInstalled)
                services.Create(Registration);
            else
                services.Configure(Registration);

            services.Start(ServiceName);
            return new(true, state == ServiceSetupState.NotInstalled
                ? "The background service is installed and running."
                : "The background service was updated and restarted.");
        }
        catch (Exception ex)
        {
            return new(false, $"Couldn't set up the background service. {ex.Message}");
        }
    }

    /// <summary>
    /// Stops and unregisters the service and deletes its files. The undo journal and logs in ProgramData stay, so a
    /// reinstall can still undo earlier changes.
    /// </summary>
    public ServiceSetupResult Remove()
    {
        try
        {
            if (services.Query(ServiceName) is not null)
            {
                services.Stop(ServiceName);
                services.Delete(ServiceName);
            }

            files.DeleteFolder(layout.InstalledFolder);
            return new(true, "The background service was removed.");
        }
        catch (Exception ex)
        {
            return new(false, $"Couldn't remove the background service. {ex.Message}");
        }
    }

    private void TryStart()
    {
        try
        {
            services.Start(ServiceName);
        }
        catch
        {
            // Reported by the caller's failure message already.
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a.Trim('"'), b, StringComparison.OrdinalIgnoreCase);
}
