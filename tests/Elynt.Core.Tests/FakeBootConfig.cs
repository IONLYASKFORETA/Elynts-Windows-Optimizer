using Elynt.Core.Boot;

namespace Elynt.Core.Tests;

/// <summary>The current boot entry's settings, in memory.</summary>
public sealed class FakeBootConfig : IBootConfig
{
    private readonly Dictionary<string, string> _elements = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string element) => _elements.GetValueOrDefault(element);

    public void Set(string element, string value) => _elements[element] = value;

    public void Delete(string element) => _elements.Remove(element);
}
