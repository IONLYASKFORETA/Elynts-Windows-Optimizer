using System.Windows;
using Serilog;

namespace Elynt.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) => Log.Error(args.Exception, "Unhandled error in the app window");
        TaskScheduler.UnobservedTaskException += (_, args) => Log.Error(args.Exception, "Unobserved background error");
        base.OnStartup(e);
    }
}
