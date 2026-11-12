namespace Elynt.Core.Tweaks;

/// <summary>Every tweak the app knows about.</summary>
public sealed class Catalogue
{
    private readonly Dictionary<string, TweakDefinition> _byId;
    private readonly Dictionary<string, PerGameTweak> _perGame;
    private readonly Dictionary<string, ChoiceGroup> _choices;
    private readonly Dictionary<string, ChoiceGroup> _choiceOf;

    /// <param name="perGame">Tweaks applied one game at a time; their templates are listed with the other tweaks.</param>
    /// <param name="choices">Pick-one choices between tweaks of this catalogue that fight each other.</param>
    public Catalogue(IEnumerable<TweakDefinition> tweaks, IEnumerable<PerGameTweak>? perGame = null, IEnumerable<ChoiceGroup>? choices = null)
    {
        _perGame = (perGame ?? []).ToDictionary(t => t.Id, StringComparer.Ordinal);
        Tweaks = [.. tweaks, .. _perGame.Values.Select(t => t.Template)];
        foreach (var tweak in Tweaks)
            Validate(tweak);
        _byId = Tweaks.ToDictionary(t => t.Id, StringComparer.Ordinal);
        _choices = (choices ?? []).ToDictionary(c => c.Id, StringComparer.Ordinal);
        _choiceOf = new(StringComparer.Ordinal);
        foreach (var choice in _choices.Values)
            ValidateChoice(choice);
    }

    public static Catalogue Default { get; } =
        new([.. GamingTweaks.All, .. PlaceboTweaks.All, .. DebloatTweaks.All, .. SecurityTweaks.All, .. WindowsUpdateTweaks.All,
             .. CleanupTweaks.All, .. AppRemovalTweaks.All],
            [.. GamingTweaks.PerGame, .. SecurityTweaks.PerGame],
            [GamingTweaks.PowerPlanChoice, WindowsUpdateTweaks.UpdateDelayChoice]);

    public IReadOnlyList<TweakDefinition> Tweaks { get; }

    public TweakDefinition Get(string id) =>
        TryGet(id, out var tweak) ? tweak : throw new KeyNotFoundException($"No tweak with id '{id}'.");

    public ChoiceGroup? Choice(string id) => _choices.GetValueOrDefault(id);

    /// <summary>The pick-one choice this tweak is an option of, or null for a tweak with its own switch.</summary>
    public ChoiceGroup? ChoiceOf(string tweakId) => _choiceOf.GetValueOrDefault(tweakId);

    /// <summary>
    /// A choice's options are this catalogue's tweaks, on one page, each in one choice only. None is set per game or
    /// destructive: choosing another option undoes the one before, so each must be undoable on its own.
    /// </summary>
    private void ValidateChoice(ChoiceGroup choice)
    {
        if (choice.Options.Count < 2)
            throw new ArgumentException($"Choice '{choice.Id}' needs two options or more; one option is a switch.");
        foreach (var option in choice.Options)
        {
            if (!_byId.TryGetValue(option.TweakId, out var tweak))
                throw new ArgumentException($"Choice '{choice.Id}' offers '{option.TweakId}', which isn't in the catalogue.");
            if (tweak.PerGame)
                Reject(tweak, $"is set per game, so it can't be an option of choice '{choice.Id}'");
            if (tweak.Reversibility == Reversibility.Destructive)
                Reject(tweak, $"is destructive, so it can't be an option of choice '{choice.Id}'");
            if (tweak.Section != _byId[choice.Options[0].TweakId].Section)
                Reject(tweak, $"is on another page from the rest of choice '{choice.Id}'");
            if (!_choiceOf.TryAdd(tweak.Id, choice))
                Reject(tweak, $"is already an option of choice '{_choiceOf[tweak.Id].Id}'");
        }
    }

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
