using System.Windows;
using Elynt.App.Theme;
using Serilog;

namespace Elynt.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) => Log.Error(args.Exception, "Unhandled error in the app window");
        TaskScheduler.UnobservedTaskException += (_, args) => Log.Error(args.Exception, "Unobserved background error");
        // App.xaml merges Fluent's dark dictionary, which normally marks the app dark too. Making sure keeps Windows' own
        // parts (the title bar) dark; Fluent then swaps that dictionary for a fresh copy in place, ahead of the app's colours.
        if (ThemeMode != ThemeMode.Dark)
            ThemeMode = ThemeMode.Dark;
        AccentWave.Install(this);
        base.OnStartup(e);
    }
}
