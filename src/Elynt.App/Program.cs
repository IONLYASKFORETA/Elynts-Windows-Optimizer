using System.Windows;
using Elynt.Core.Setup;
using Elynt.Windows;
using Velopack;

namespace Elynt.App;

/// <summary>
/// The entry point. Velopack's install, update and uninstall hooks run first; then the elevated helper runs if
/// asked for; then the Windows 11 check; then the window.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack starts the app with its own arguments during install, update and uninstall, handles them here and
        // exits. Hooks can't show UI and must finish within 30 s.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => ServiceBootstrap.RemoveBeforeUninstall())
            .Run();

        AppLog.Start();
        try
        {
            if (args.Contains(ServiceBootstrap.RemoveArgument))
                return ServiceBootstrap.RunElevatedRemove();

            var support = OsSupport.Check(WindowsSystemInfo.Describe());
            if (!support.Supported)
            {
                AppLog.Warning("Refusing to run: {Message}", support.Message);
                // The app's resources hold its look, which the note needs; the app itself never runs.
                new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }.InitializeComponent();
                ConfirmDialog.Tell(null, support.Message, "Elynt's Windows Optimizer");
                return 1;
            }

            if (args.Contains(ServiceBootstrap.SetupArgument))
                return ServiceBootstrap.RunElevatedSetup();

            var app = new App();
            app.InitializeComponent();
            return app.Run();
        }
        finally
        {
            AppLog.Stop();
        }
    }
}
