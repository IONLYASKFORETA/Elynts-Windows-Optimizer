namespace Elynt.Core.Repair;

/// <summary>Reads what Windows' repair programs' exit codes and output mean, in plain words.</summary>
public static class ToolOutput
{
    /// <summary>Exit codes installers use for "worked, restart to finish".</summary>
    private const int RestartRequired = 3010;
    private const int RestartInitiated = 1641;

    /// <summary>winget's APPINSTALLER_CLI_ERROR_INSTALL_REBOOT_REQUIRED_TO_FINISH (0x8A150109).</summary>
    private const int WingetRestartToFinish = unchecked((int)0x8A150109);

    public static StepOutcome Dism(int exitCode, string output)
    {
        if (exitCode == 0)
            return output.Contains("corruption was repaired", StringComparison.OrdinalIgnoreCase)
                ? StepOutcome.Ok("Found damage in Windows' component store and repaired it.")
                : StepOutcome.Ok("Windows' component store is healthy.");

        var hint = exitCode == unchecked((int)0x800F081F)
            ? " It couldn't download clean files from Windows Update; check the internet connection and try again."
            : "";
        return StepOutcome.Failed($"DISM couldn't finish (error {Hex(exitCode)}).{hint}");
    }

    public static StepOutcome Sfc(int exitCode, string output)
    {
        if (Has(output, "did not find any integrity violations"))
            return StepOutcome.Ok("No damaged system files found.");
        if (Has(output, "successfully repaired"))
            return StepOutcome.Ok("Found damaged system files and repaired them.", restartNeeded: Has(output, "reboot") || Has(output, "restart"));
        if (Has(output, "unable to fix"))
            return new(StepStatus.ProblemsFound, "Found damaged system files it couldn't repair. The log lists them; send it to Aden.");
        if (Has(output, "repair pending"))
            return new(StepStatus.Failed, "Windows has a repair waiting for a restart. Restart the PC, then run this again.", RestartNeeded: true);
        return exitCode == 0
            ? StepOutcome.Ok("SFC finished.")
            : StepOutcome.Failed($"SFC couldn't finish (exit code {exitCode}).");
    }

    public static StepOutcome DiskScan(int exitCode, string output)
    {
        if (Has(output, "found no problems"))
            return StepOutcome.Ok("No problems found on the Windows drive.");
        if (Has(output, "found problems") || Has(output, "corruption") || Has(output, "/spotfix") || Has(output, "/offlinescanandfix"))
            return new(StepStatus.ProblemsFound, "Found problems on the Windows drive. You can have them fixed at the next restart.",
                FollowUpToolId: RepairToolIds.DiskFixAtRestart);
        return exitCode == 0
            ? StepOutcome.Ok("No problems found on the Windows drive.")
            : StepOutcome.Failed($"The disk check couldn't finish (exit code {exitCode}).");
    }

    public static StepOutcome ScheduleDiskFix(int exitCode, string output) => exitCode == 0
        ? StepOutcome.Ok("The Windows drive will be checked and fixed at the next restart. It can take a while; don't turn the PC off.", restartNeeded: true)
        : StepOutcome.Failed($"Couldn't schedule the fix (exit code {exitCode}).");

    /// <summary>net stop/start: exit code 2 also means the service was already stopped or already running.</summary>
    public static Func<int, string, StepOutcome> ServiceControl(string done) => (exitCode, output) => exitCode switch
    {
        0 => StepOutcome.Ok(done),
        2 => StepOutcome.Ok($"{done} (it already was)"),
        _ => StepOutcome.Failed($"Failed (exit code {exitCode}). {LastLine(output)}".TrimEnd()),
    };

    public static Func<int, string, StepOutcome> Simple(string done, bool restartNeeded = false) => (exitCode, output) => exitCode == 0
        ? StepOutcome.Ok(done, restartNeeded)
        : StepOutcome.Failed($"Failed (exit code {exitCode}). {LastLine(output)}".TrimEnd());

    public static Func<int, string, StepOutcome> Winget(string name) => (exitCode, output) => exitCode switch
    {
        0 => StepOutcome.Ok("Reinstalled."),
        RestartRequired or RestartInitiated or WingetRestartToFinish => StepOutcome.Ok("Reinstalled; restart to finish.", restartNeeded: true),
        _ => StepOutcome.Failed($"Couldn't reinstall {name} (error {Hex(exitCode)}). {LastLine(output)}".TrimEnd()),
    };

    private static bool Has(string output, string text) => output.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static string Hex(int code) => $"0x{code:X8}";

    private static string LastLine(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
}
