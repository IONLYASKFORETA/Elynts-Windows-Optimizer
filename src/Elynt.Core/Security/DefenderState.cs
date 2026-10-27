using Elynt.Core.Repair;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Security;

/// <summary>
/// Microsoft Defender's Tamper Protection and running state, read only. The app reads these (Get-MpComputerStatus,
/// or the documented registry values) but never changes Tamper Protection itself: only the user can, by hand, in
/// Windows Security. While Tamper Protection is on, Windows reverts any change to Defender's policy, so the app
/// refuses to try and shows the user how to turn Tamper Protection off first.
/// </summary>
public interface IDefenderState
{
    /// <summary>
    /// Whether Tamper Protection is on (Get-MpComputerStatus's IsTamperProtected, or the Features\TamperProtection
    /// registry value). On real PCs and on CI runners it is on by default.
    /// </summary>
    bool TamperProtectionOn { get; }

    /// <summary>Whether Defender's real-time antivirus is currently running. Shown to the user; not used to gate anything.</summary>
    bool DefenderRunning { get; }
}

/// <summary>
/// Refuses the Defender-off tweak while Tamper Protection is on, with a step-by-step guide for turning it off by hand.
/// The app never turns Tamper Protection off itself, and if it can't read Defender's state at all it fails safe and
/// refuses, so it never writes a Defender policy change it can't be sure will hold.
/// </summary>
public sealed class TamperProtectionGuard : IApplyGuard
{
    public static TamperProtectionGuard Instance { get; } = new();

    /// <summary>The plain-language guide shown when the tweak is blocked. Steps match Windows 11's Windows Security app.</summary>
    public const string Guide =
        "Turn off Tamper Protection first, by hand, in Windows Security. The app never does this for you, and can't: " +
        "Tamper Protection exists to stop any program (this one included) changing Microsoft Defender. " +
        "To turn it off: 1) Open Windows Security (press Start, type \"Windows Security\", open it). If it opens as a " +
        "blank white window, fix that first with \"" + RepairCatalogue.WindowsSecurityAppName + "\" in this app's " +
        "Repair section. 2) Go to Virus & threat protection. 3) Under \"Virus & threat protection settings\", click Manage settings. " +
        "4) Switch Tamper Protection off, and accept the Windows prompt. " +
        "Then come back here and turn this on again. When you're done, you can turn Tamper Protection back on in the " +
        "same place; turning Microsoft Defender back on here does not need it off.";

    private const string CantRead =
        "The app can't read Microsoft Defender's state on this PC, so it can't confirm Tamper Protection is off. For " +
        "safety it won't change Defender until it can. ";

    public string? Blocking(SystemAdapters system)
    {
        if (system.DefenderState is not { } defender)
            return CantRead + Guide;

        try
        {
            return defender.TamperProtectionOn ? Guide : null;
        }
        catch (Exception)
        {
            // Refuse rather than throw, so the page still shows why and the switch fails with plain words, not a
            // PowerShell error.
            return CantRead + "Defender didn't answer when the app asked, which happens when its service is stopped " +
                   "or parts of it were removed. " + Guide;
        }
    }
}
