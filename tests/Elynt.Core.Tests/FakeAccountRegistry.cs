using Elynt.Core.Registry;

namespace Elynt.Core.Tests;

/// <summary>
/// One account's view of a PC's registry, as the service sees it for that account: HKLM is the PC's own, shared with
/// every other account, and HKCU is this account's alone.
/// </summary>
public sealed class FakeAccountRegistry(FakeRegistry machine, FakeRegistry account) : IRegistry
{
    private FakeRegistry For(RegistryLocation location) => location.IsMachineWide ? machine : account;

    /// <summary>A key lives where its own default value does.</summary>
    private FakeRegistry For(RegistryRoot root, string keyPath) => For(new RegistryLocation(root, keyPath, ""));

    public RegistryValue? GetValue(RegistryLocation location) => For(location).GetValue(location);

    public void SetValue(RegistryLocation location, RegistryValue value) => For(location).SetValue(location, value);

    public void DeleteValue(RegistryLocation location) => For(location).DeleteValue(location);

    public bool KeyExists(RegistryRoot root, string keyPath) => For(root, keyPath).KeyExists(root, keyPath);

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string keyPath) => For(root, keyPath).GetSubKeyNames(root, keyPath);

    public void DeleteKeyIfEmpty(RegistryRoot root, string keyPath) => For(root, keyPath).DeleteKeyIfEmpty(root, keyPath);
}
