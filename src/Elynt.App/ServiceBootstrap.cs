using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Elynt.Core.Setup;
using Elynt.Windows;
using Serilog;

namespace Elynt.App;

/// <summary>
/// Installs, updates and removes the background service. The window runs without admin rights, so it starts a copy
/// of itself elevated (one UAC prompt) that does the work and exits. The service is copied out of the app's
/// per-user folder into Program Files, because a SYSTEM service must not run from a folder the user can write.
/// </summary>
public static class ServiceBootstrap
{
    public const string SetupArgument = "--setup-service";
    public const string RemoveArgument = "--remove-service";

    private static ServiceLayout Layout { get; } = new(
        Path.Combine(AppContext.BaseDirectory, "service"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ElyntOptimizer", "service"));

    private static ServiceSetup NewSetup() => new(new WindowsServiceControl(), new WindowsServiceFiles(), Layout);

    /// <summary>Reads only, so it works without admin rights.</summary>
    public static ServiceSetupState Check()
    {
        try
        {
            return NewSetup().Check();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't check the background service");
            return ServiceSetupState.NoBundledService;
        }
    }

    public static bool NeedsSetup(ServiceSetupState state) =>
        state is ServiceSetupState.NotInstalled or ServiceSetupState.Outdated or ServiceSetupState.Stopped;

    /// <summary>Runs the elevated helper and waits. False when the user declined the UAC prompt or setup failed.</summary>
    public static Task<bool> SetUpElevatedAsync() => Task.Run(() => RunSelfElevated(SetupArgument, TimeSpan.FromMinutes(2)));

    /// <summary>The elevated helper's work for <see cref="SetupArgument"/>.</summary>
    public static int RunElevatedSetup()
    {
        var result = NewSetup().InstallOrUpdate();
        Log.Information("Service setup: {Succeeded} {Message}", result.Succeeded, result.Message);
        return result.Succeeded ? 0 : 1;
    }

    /// <summary>The elevated helper's work for <see cref="RemoveArgument"/>.</summary>
    public static int RunElevatedRemove()
    {
        var result = NewSetup().Remove();
        Log.Information("Service removal: {Succeeded} {Message}", result.Succeeded, result.Message);
        return result.Succeeded ? 0 : 1;
    }

    /// <summary>
    /// Velopack's uninstall hook: removing the app also removes its service. The hook runs without admin rights, so
    /// it asks for elevation; if the user says no, the service stays and the friend can run the app's uninstall
    /// again or remove it by hand.
    /// </summary>
    public static void RemoveBeforeUninstall()
    {
        try
        {
            if (new WindowsServiceControl().Query(ServiceSetup.ServiceName) is null)
                return;
            RunSelfElevated(RemoveArgument, TimeSpan.FromSeconds(25));
        }
        catch
        {
            // An uninstall hook must never fail the uninstall.
        }
    }

    private static bool RunSelfElevated(string argument, TimeSpan timeout)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Couldn't find the app's own path.");
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, argument)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            if (process is null)
                return false;
            if (!process.WaitForExit(timeout))
            {
                Log.Warning("The elevated helper ({Argument}) is still running after {Timeout}", argument, timeout);
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log.Information("The user declined the UAC prompt for {Argument}", argument);
            return false;
        }
    }
}
