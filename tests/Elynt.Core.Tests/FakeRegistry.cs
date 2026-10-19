using Elynt.Core.Registry;

namespace Elynt.Core.Tests;

/// <summary>An in-memory registry. Keys are case-insensitive, like the real one.</summary>
public sealed class FakeRegistry : IRegistry
{
    private readonly Dictionary<(RegistryRoot, string), Dictionary<string, RegistryValue>> _keys = new(new KeyComparer());

    private int? _writesUntilFailure;
    private bool _dieOnFailure;
    private bool _dead;

    /// <summary>Lets <paramref name="writes"/> more writes succeed, fails the next one, then works again.</summary>
    public void FailOnceAfter(int writes) => (_writesUntilFailure, _dieOnFailure) = (writes, false);

    /// <summary>Lets <paramref name="writes"/> more writes succeed, then fails every write, as if the service died.</summary>
    public void DieAfter(int writes) => (_writesUntilFailure, _dieOnFailure) = (writes, true);

    /// <summary>Brings the registry back after <see cref="DieAfter"/>, as a restarted service would see it.</summary>
    public void Revive() => _dead = false;

    public RegistryValue? GetValue(RegistryLocation location) =>
        _keys.TryGetValue((location.Root, location.KeyPath), out var values) && values.TryGetValue(location.ValueName, out var value)
            ? value
            : null;

    public void SetValue(RegistryLocation location, RegistryValue value)
    {
        ThrowIfBroken();
        CreateKey(location.Root, location.KeyPath);
        _keys[(location.Root, location.KeyPath)][location.ValueName] = value;
    }

    public void DeleteValue(RegistryLocation location)
    {
        ThrowIfBroken();
        if (_keys.TryGetValue((location.Root, location.KeyPath), out var values))
            values.Remove(location.ValueName);
    }

    public bool KeyExists(RegistryRoot root, string keyPath) => _keys.ContainsKey((root, keyPath));

    /// <summary>Nothing was ever written.</summary>
    public bool IsEmpty => _keys.Count == 0;

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string keyPath) =>
        _keys.Keys
            .Where(k => k.Item1 == root && k.Item2.StartsWith(keyPath + "\\", StringComparison.OrdinalIgnoreCase))
            .Select(k => k.Item2[(keyPath.Length + 1)..])
            .Where(rest => !rest.Contains('\\'))
            .ToList();

    public void DeleteKeyIfEmpty(RegistryRoot root, string keyPath)
    {
        ThrowIfBroken();
        var hasSubkeys = _keys.Keys.Any(k => k.Item1 == root && k.Item2.StartsWith(keyPath + "\\", StringComparison.OrdinalIgnoreCase));
        if (_keys.TryGetValue((root, keyPath), out var values) && values.Count == 0 && !hasSubkeys)
            _keys.Remove((root, keyPath));
    }

    /// <summary>Creates a key and all its parents, as RegCreateKeyEx does.</summary>
    public void CreateKey(RegistryRoot root, string keyPath)
    {
        var parts = keyPath.Split('\\');
        for (var i = 1; i <= parts.Length; i++)
        {
            var path = string.Join('\\', parts[..i]);
            if (!_keys.ContainsKey((root, path)))
                _keys[(root, path)] = new Dictionary<string, RegistryValue>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void ThrowIfBroken()
    {
        if (_dead)
            throw new IOException("The registry is unavailable.");
        if (_writesUntilFailure is 0)
        {
            _writesUntilFailure = null;
            _dead = _dieOnFailure;
            throw new IOException("The registry is unavailable.");
        }
        if (_writesUntilFailure is { } n)
            _writesUntilFailure = n - 1;
    }

    private sealed class KeyComparer : IEqualityComparer<(RegistryRoot, string)>
    {
        public bool Equals((RegistryRoot, string) x, (RegistryRoot, string) y) =>
            x.Item1 == y.Item1 && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((RegistryRoot, string) obj) =>
            HashCode.Combine(obj.Item1, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2));
    }
}
