using Elynt.Core.Security;

namespace Elynt.Core.Tests;

/// <summary>
/// Microsoft Defender's Tamper Protection and running state in memory. The app only ever reads these; it never turns
/// Tamper Protection off itself. Tamper Protection defaults to on, as it is on a real PC and on CI runners.
/// </summary>
public sealed class FakeDefenderState : IDefenderState
{
    private bool _tamperProtectionOn = true;
    private bool _defenderRunning = true;

    /// <summary>Reading throws, as Get-MpComputerStatus does when Defender's service is stopped or removed.</summary>
    public bool ReadFails { get; set; }

    public bool TamperProtectionOn
    {
        get => ReadFails ? throw new InvalidOperationException("Get-MpComputerStatus failed (exit code 1).") : _tamperProtectionOn;
        set => _tamperProtectionOn = value;
    }

    public bool DefenderRunning
    {
        get => ReadFails ? throw new InvalidOperationException("Get-MpComputerStatus failed (exit code 1).") : _defenderRunning;
        set => _defenderRunning = value;
    }
}
