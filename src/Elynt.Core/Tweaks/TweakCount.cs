namespace Elynt.Core.Tweaks;

/// <summary>How many tweaks are on, out of those that fit this PC: a tweak section's "5 of 20 on".</summary>
public readonly record struct TweakCount(int On, int Total)
{
    /// <summary>
    /// Counts only the tweaks that fit this PC and have a switch. One that doesn't fit counts neither as on nor towards
    /// the total, and nor does a per-game choice, which is set one game at a time (each game then gets its own switch).
    /// </summary>
    public static TweakCount Of(IEnumerable<TweakView> tweaks)
    {
        var counted = tweaks.Where(t => t.Applicable && !t.PerGame).ToList();
        return new(counted.Count(t => t.State == TweakState.On), counted.Count);
    }

    public string Text => $"{On} of {Total} on";
}
