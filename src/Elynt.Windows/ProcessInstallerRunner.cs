using System.Diagnostics;
using Elynt.Core.Drivers;

namespace Elynt.Windows;

/// <summary>Runs an installer as a hidden child process of the service and waits for its exit code.</summary>
/// <param name="timeout">How long a silent install may take before it is treated as hung (default 30 minutes).</param>
public sealed class ProcessInstallerRunner(TimeSpan? timeout = null) : IInstallerRunner
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromMinutes(30);

    public int RunInstaller(string path, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path)!,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Couldn't start {path}.");
        if (!process.WaitForExit(_timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"The installer was still running after {_timeout.TotalMinutes:0} minutes and was stopped.");
        }

        return process.ExitCode;
    }
}
