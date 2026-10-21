using Elynt.Core.Security;

namespace Elynt.Windows;

/// <summary>
/// Microsoft Defender's Tamper Protection and running state, read through Defender's own PowerShell module
/// (Get-MpComputerStatus), the documented way. The app only ever reads these; it never turns Tamper Protection off.
/// Reading needs admin rights, which the service has.
/// </summary>
public sealed class WindowsDefenderState : IDefenderState
{
    public bool TamperProtectionOn => Read().Tamper;

    public bool DefenderRunning => Read().RealTime;

    /// <summary>
    /// Both facts in one call. Get-MpComputerStatus's IsTamperProtected reflects Tamper Protection; a failure to read
    /// is treated as Tamper Protection on, so the Defender-off tweak fails safe and never writes a change it can't be
    /// sure will hold.
    /// </summary>
    private (bool Tamper, bool RealTime) Read()
    {
        var output = PowerShell.Run(
            "Import-Module Defender; $s = Get-MpComputerStatus; " +
            "\"$($s.IsTamperProtected)|$($s.RealTimeProtectionEnabled)\"",
            TimeSpan.FromSeconds(30));
        var parts = output.Trim().Split('|');
        // Anything we can't parse as an explicit "off" is taken as on, the safe reading.
        var tamper = parts.Length < 1 || !IsFalse(parts[0]);
        var realTime = parts.Length >= 2 && !IsFalse(parts[1]);
        return (tamper, realTime);
    }

    private static bool IsFalse(string value) =>
        value.Trim().Equals("False", StringComparison.OrdinalIgnoreCase) || value.Trim() == "0";
}
