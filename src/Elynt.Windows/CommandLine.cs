namespace Elynt.Windows;

/// <summary>Short calls to a quick System32 tool (30 seconds at most), through <see cref="SystemCommand"/>.</summary>
internal static class CommandLine
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static string Run(string tool, params string[] arguments) => SystemCommand.Run(tool, arguments, Timeout);

    /// <param name="alsoSucceeded">Exit codes other than 0 that mean success, e.g. 3010 (restart needed).</param>
    public static string Run(string tool, int[] alsoSucceeded, params string[] arguments) =>
        SystemCommand.Run(tool, arguments, Timeout, alsoSucceeded);
}
