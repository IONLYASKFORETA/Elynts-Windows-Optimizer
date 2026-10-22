using Elynt.Core.Security;

namespace Elynt.Core.Tests;

/// <summary>
/// Microsoft Defender's Tamper Protection and running state in memory. The app only ever reads these; it never turns
/// Tamper Protection off itself. Tamper Protection defaults to on, as it is on a real PC and on CI runners.
/// </summary>
public sealed class FakeDefenderState : IDefenderState
{
    public bool TamperProtectionOn { get; set; } = true;

    public bool DefenderRunning { get; set; } = true;
}
