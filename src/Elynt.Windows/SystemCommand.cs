using System.Diagnostics;

namespace Elynt.Windows;

/// <summary>
/// Runs a Windows tool from System32 by full path, so nothing earlier on PATH can stand in for it. The one place the
/// Windows adapters start a system tool (powercfg, bcdedit, pnputil, sc, PowerShell and the rest).
/// </summary>
internal static class SystemCommand
{
    public static string System32 { get; } = Environment.GetFolderPath(Environment.SpecialFolder.System);

    /// <summary>Runs the tool and returns its output. Throws with the tool's own words when it fails.</summary>
    /// <param name="timeout">How long the tool may run; five minutes when null. It is killed after that.</param>
    /// <param name="alsoSucceeded">Exit codes other than 0 that mean success, e.g. 3010 (restart needed).</param>
    public static string Run(string tool, IEnumerable<string> arguments, TimeSpan? timeout = null, IReadOnlyCollection<int>? alsoSucceeded = null)
    {
        var list = arguments.ToList();
        var start = new ProcessStartInfo(Path.Combine(System32, tool))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in list)
            start.ArgumentList.Add(argument);

        var limit = timeout ?? TimeSpan.FromMinutes(5);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Couldn't start {tool}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(limit))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{tool} didn't finish within {Describe(limit)}.");
        }

        var text = output.GetAwaiter().GetResult();
        if (process.ExitCode != 0 && alsoSucceeded?.Contains(process.ExitCode) != true)
        {
            var message = (error.GetAwaiter().GetResult() + " " + text).Trim();
            throw new InvalidOperationException($"{tool} {string.Join(' ', list)} failed (exit code {process.ExitCode}): {message}");
        }
        return text;
    }

    private static string Describe(TimeSpan limit) =>
        limit.TotalMinutes >= 1 ? $"{limit.TotalMinutes:0} minutes" : $"{limit.TotalSeconds:0} seconds";
}
