namespace Elynt.Core.Tweaks;

/// <summary>Every tweak the app knows about.</summary>
public sealed class Catalogue
{
    private readonly Dictionary<string, TweakDefinition> _byId;
    private readonly Dictionary<string, PerGameTweak> _perGame;

    /// <param name="perGame">Tweaks applied one game at a time; their templates are listed with the other tweaks.</param>
    public Catalogue(IEnumerable<TweakDefinition> tweaks, IEnumerable<PerGameTweak>? perGame = null)
    {
        _perGame = (perGame ?? []).ToDictionary(t => t.Id, StringComparer.Ordinal);
        Tweaks = [.. tweaks, .. _perGame.Values.Select(t => t.Template)];
        foreach (var tweak in Tweaks)
            Validate(tweak);
        _byId = Tweaks.ToDictionary(t => t.Id, StringComparer.Ordinal);
    }

    public static Catalogue Default { get; } =
        new([.. GamingTweaks.All, .. DebloatTweaks.All, .. WindowsUpdateTweaks.All, .. AppRemovalTweaks.All], GamingTweaks.PerGame);

    public IReadOnlyList<TweakDefinition> Tweaks { get; }

    public TweakDefinition Get(string id) =>
        TryGet(id, out var tweak) ? tweak : throw new KeyNotFoundException($"No tweak with id '{id}'.");

    /// <summary>Labels that contradict each other are a catalogue bug, caught when the catalogue is built.</summary>
    private static void Validate(TweakDefinition tweak)
    {
        if (tweak.Reversibility == Reversibility.Destructive && string.IsNullOrWhiteSpace(tweak.DestructiveWarning))
            Reject(tweak, "is destructive, so it must spell out what can't be undone");

        if (!tweak.ProfileEligible)
            return;
        if (tweak.Risks.HasFlag(RiskFlags.NeedsReboot))
            Reject(tweak, "needs a reboot, so it can't be in a per-game profile");
        if (tweak.Reversibility == Reversibility.Destructive)
            Reject(tweak, "is destructive, so it can't be in a per-game profile");
        if (tweak.Section == Sections.Security)
            Reject(tweak, "is in Security; Defender and other Security tweaks are never in per-game profiles");
        if (tweak.Refresh != SessionRefresh.None)
            Reject(tweak, "needs the signed-in session to refresh, so it can't be applied by a per-game profile");
    }

    private static void Reject(TweakDefinition tweak, string problem) =>
        throw new ArgumentException($"Tweak '{tweak.Id}' {problem}.");

    /// <summary>Finds a tweak by id, including one game's instance of a per-game tweak (<c>template-id|exe path</c>).</summary>
    public bool TryGet(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TweakDefinition? tweak)
    {
        if (_byId.TryGetValue(id, out tweak))
            return true;
        var separator = id.IndexOf(PerGameTweak.Separator);
        tweak = separator > 0 && _perGame.TryGetValue(id[..separator], out var perGame) ? perGame.For(id[(separator + 1)..]) : null;
        return tweak is not null;
    }
}
