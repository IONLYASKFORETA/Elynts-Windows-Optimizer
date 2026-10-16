using System.Text;
using Elynt.Core.Safety;

namespace Elynt.Windows;

/// <summary>
/// System Restore through Windows PowerShell 5.1's Checkpoint-Computer (it is not in PowerShell 7). Windows skips a
/// new point without failing when one was made in the last 24 hours, so this compares the newest restore point before
/// and after to tell whether one was really made.
/// </summary>
public sealed class WindowsRestorePoints : IRestorePoints
{
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        function Newest { @(Get-ComputerRestorePoint -ErrorAction SilentlyContinue | Sort-Object SequenceNumber | Select-Object -Last 1 -ExpandProperty SequenceNumber) }
        $before = Newest
        $warn = $null
        try {
            Checkpoint-Computer -Description $description -RestorePointType MODIFY_SETTINGS -WarningVariable warn -WarningAction SilentlyContinue
        } catch {
            Write-Output ('FAILED ' + $_.Exception.Message)
            exit 0
        }
        $after = Newest
        if ($after -and "$after" -ne "$before") { 'CREATED' }
        elseif ($warn) { 'SKIPPED ' + ($warn -join ' ') }
        else { 'SKIPPED' }
        """;

    public RestorePointResult TryCreate(string description)
    {
        var quoted = "'" + description.Replace("'", "''") + "'";
        var command = $"$description = {quoted}\n{Script}";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        var output = SystemCommand.Run(
            Path.Combine("WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded],
            TimeSpan.FromMinutes(3)).Trim();

        if (output.EndsWith("CREATED", StringComparison.Ordinal))
            return RestorePointResult.Created();

        var reason = output.Contains("FAILED ", StringComparison.Ordinal)
            ? output[(output.LastIndexOf("FAILED ", StringComparison.Ordinal) + 7)..].Trim() +
              " System Protection may be turned off for drive C: (Control Panel > System > System Protection)."
            : output.Contains("SKIPPED ", StringComparison.Ordinal)
                ? output[(output.LastIndexOf("SKIPPED ", StringComparison.Ordinal) + 8)..].Trim()
                : "Windows didn't add a new restore point. It makes at most one a day, or System Protection may be off.";
        return RestorePointResult.Skipped(reason);
    }
}
