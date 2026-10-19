using System.Diagnostics;

namespace Elynt.Windows;

/// <summary>Runs a Windows tool from System32 and returns its output, throwing when it fails.</summary>
internal static class CommandLine
{
    public static string Run(string tool, params string[] arguments) => Run(tool, [], arguments);

    /// <param name="alsoSucceeded">Exit codes other than 0 that mean success, e.g. 3010 (restart needed).</param>
    public static string Run(string tool, int[] alsoSucceeded, params string[] arguments)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), tool);
        var start = new ProcessStartInfo(path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Couldn't start {tool}.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            process.Kill();
            throw new TimeoutException($"{tool} didn't finish within 30 seconds.");
        }
        if (process.ExitCode != 0 && !alsoSucceeded.Contains(process.ExitCode))
            throw new InvalidOperationException(
                $"{tool} {string.Join(' ', arguments)} failed (exit code {process.ExitCode}): {(output + error.Result).Trim()}");
        return output;
    }
}
