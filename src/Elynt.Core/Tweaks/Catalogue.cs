namespace Elynt.Core.Tweaks;

/// <summary>Every tweak the app knows about.</summary>
public sealed class Catalogue
{
    private readonly Dictionary<string, TweakDefinition> _byId;

    public Catalogue(IEnumerable<TweakDefinition> tweaks)
    {
        Tweaks = tweaks.ToList();
        _byId = Tweaks.ToDictionary(t => t.Id, StringComparer.Ordinal);
    }

    public static Catalogue Default { get; } = new(GamingTweaks.All);

    public IReadOnlyList<TweakDefinition> Tweaks { get; }

    public TweakDefinition Get(string id) =>
        _byId.TryGetValue(id, out var tweak) ? tweak : throw new KeyNotFoundException($"No tweak with id '{id}'.");

    public bool TryGet(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TweakDefinition? tweak) =>
        _byId.TryGetValue(id, out tweak);
}
