using Elynt.Core.Apps;

namespace Elynt.Core.Tests;

/// <summary>In-memory Windows optional features. A feature not added isn't on this PC.</summary>
public sealed class FakeOptionalFeatures : IOptionalFeatures
{
    private readonly Dictionary<string, bool> _features = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string name, bool enabled) => _features[name] = enabled;

    public bool? IsEnabled(string name) => _features.TryGetValue(name, out var enabled) ? enabled : null;

    public void Enable(string name) => Set(name, true);

    public void Disable(string name) => Set(name, false);

    private void Set(string name, bool enabled)
    {
        if (!_features.ContainsKey(name))
            throw new InvalidOperationException($"The feature {name} isn't on this PC.");
        _features[name] = enabled;
    }
}
