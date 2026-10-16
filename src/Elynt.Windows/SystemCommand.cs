using System.Diagnostics;

namespace Elynt.Windows;

/// <summary>Runs a Windows tool from System32 by full path, so nothing earlier on PATH can stand in for it.</summary>
internal static class SystemCommand
{
    public static string System32 { get; } = Environment.GetFolderPath(Environment.SpecialFolder.System);

    /// <summary>Runs the tool and returns its output. Throws with the tool's own words when it fails.</summary>
    public static string Run(string tool, IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        var start = new ProcessStartInfo(Path.Combine(System32, tool))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Couldn't start {tool}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout ?? TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{tool} didn't finish in time.");
        }

        var text = output.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            var message = (error.GetAwaiter().GetResult() + " " + text).Trim();
            throw new InvalidOperationException($"{tool} failed (exit code {process.ExitCode}): {message}");
        }
        return text;
    }
}
