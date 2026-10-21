namespace Elynt.Core.Tweaks;

/// <summary>
/// A transient, on-the-PC reason a tweak can't be applied right now, with a plain-language guide for clearing it.
/// Unlike <see cref="Applicability"/> (a fixed mismatch with this PC's hardware, edition or build), a guard reflects
/// a state the user can change by hand and then try again (e.g. Microsoft Defender's Tamper Protection being on).
/// The Engine refuses to apply while it is set, and the UI shows the guide and greys the switch.
/// </summary>
public interface IApplyGuard
{
    /// <summary>Why the tweak can't be applied now, with how to clear it, or null when it may go ahead.</summary>
    string? Blocking(SystemAdapters system);
}
