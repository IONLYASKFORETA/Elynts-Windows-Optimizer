namespace Elynt.Core.Tweaks;

/// <summary>How many tweaks are on, out of those that fit this PC: a tweak section's "5 of 20 on".</summary>
public readonly record struct TweakCount(int On, int Total)
{
    /// <summary>
    /// Counts only the tweaks that fit this PC and have a switch. One that doesn't fit counts neither as on nor towards
    /// the total, and nor does a per-game choice, which is set one game at a time (each game then gets its own switch).
    /// A pick-one choice's options share one row, so they count once: on when one of them is.
    /// </summary>
    public static TweakCount Of(IEnumerable<TweakView> tweaks)
    {
        var rows = tweaks
            .Where(t => t.Applicable && !t.PerGame)
            .GroupBy(t => (t.Choice?.Id, Tweak: t.Choice is null ? t.Id : null))
            .ToList();
        return new(rows.Count(row => row.Any(t => t.State == TweakState.On)), rows.Count);
    }

    public string Text => $"{On} of {Total} on";
}
