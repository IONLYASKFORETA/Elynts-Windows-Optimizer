using Elynt.Core.Registry;

namespace Elynt.Core.Tests;

/// <summary>
/// One account's view of a PC's registry, as the service sees it for that account: HKLM is the PC's own, shared with
/// every other account, and HKCU is this account's alone.
/// </summary>
public sealed class FakeAccountRegistry(FakeRegistry machine, FakeRegistry account) : IRegistry
{
    private FakeRegistry For(RegistryRoot root) => root == RegistryRoot.LocalMachine ? machine : account;

    public RegistryValue? GetValue(RegistryLocation location) => For(location.Root).GetValue(location);

    public void SetValue(RegistryLocation location, RegistryValue value) => For(location.Root).SetValue(location, value);

    public void DeleteValue(RegistryLocation location) => For(location.Root).DeleteValue(location);

    public bool KeyExists(RegistryRoot root, string keyPath) => For(root).KeyExists(root, keyPath);

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string keyPath) => For(root).GetSubKeyNames(root, keyPath);

    public void DeleteKeyIfEmpty(RegistryRoot root, string keyPath) => For(root).DeleteKeyIfEmpty(root, keyPath);
}
