using System.IO;
using Serilog;

namespace Elynt.App;

/// <summary>The app's rolling log in %LocalAppData%\ElyntOptimizer\logs, shared by the window and the elevated helper.</summary>
public static class AppLog
{
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ElyntOptimizer", "logs");

    public static void Start()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(Folder, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                // The window and the elevated helper may write at the same time.
                shared: true)
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Fatal(args.ExceptionObject as Exception, "The app crashed");
        Log.Information("App started ({Args})", string.Join(' ', Environment.GetCommandLineArgs().Skip(1)));
    }

    public static void Warning(string template, string value) => Log.Warning(template, value);

    public static void Stop()
    {
        Log.Information("App closed");
        Log.CloseAndFlush();
    }
}
