using System.IO;
using System.IO.Pipes;
using System.Windows;
using Elynt.Core;
using Elynt.Core.Ipc;
using Elynt.Core.Tests;

namespace Elynt.App.Screenshots;

/// <summary>
/// Serves an engine on the test fakes under the app's own pipe name, opens the real window against it and saves a
/// screenshot of every page. Bypasses the app's Program.Main, so it also runs on Windows Server CI machines.
/// </summary>
/// <remarks>Usage: <c>Elynt.App.Screenshots [output folder]</c> (default: ui-shots).</remarks>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "ui-shots");
        using var stop = new CancellationTokenSource();
        // The sample PC's driver backups and the tour's settings file go here, deleted at the end. The tour flips the
        // Animated accents switch, which saves: a throwaway settings file keeps that out of the user's own file, and
        // starts every tour with nothing saved.
        var scratch = Directory.CreateTempSubdirectory("elynt-tour-");
        var engine = SamplePc.NewEngine(scratch.FullName);
        var server = new EnginePipeServer(
            () => new NamedPipeServerStream(
                EngineProtocol.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous),
            _ => engine);
        var serving = Task.Run(() => server.RunAsync(stop.Token));

        UserSettings.File = new AppSettingsFile(Path.Combine(scratch.FullName, "settings.json"));

        var bindingErrors = BindingErrorLog.Listen();
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        app.Startup += async (_, _) =>
        {
            var exitCode = 1;
            try
            {
                // App.xaml's StartupUri opens the window once this handler first yields.
                await Tour.RunAsync(app, output);
                exitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
            finally
            {
                app.Shutdown(exitCode);
            }
        };
        var result = app.Run();

        stop.Cancel();
        try
        {
            serving.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Stopping the server cancels its wait for the next client.
        }
        scratch.Delete(recursive: true);
        Console.WriteLine($"Screenshots in {output}");
        // Reported, not fatal here, so the pictures still get published; the workflow fails on this file.
        if (bindingErrors.Errors.Count > 0)
        {
            File.WriteAllLines(Path.Combine(output, "binding-errors.txt"), bindingErrors.Errors);
            Console.WriteLine($"{bindingErrors.Errors.Count} binding errors:");
            foreach (var error in bindingErrors.Errors)
                Console.WriteLine(error);
        }
        return result;
    }
}
