namespace Elynt.Core.Registry;

/// <summary>
/// The registry adapter. <see cref="RegistryRoot.CurrentUser"/> means the signed-in user who sent the command,
/// not the account the service runs as.
/// </summary>
public interface IRegistry
{
    /// <summary>The value's exact type and bytes, or null when the value (or its key) does not exist.</summary>
    RegistryValue? GetValue(RegistryLocation location);

    /// <summary>Writes the value, creating the key and any missing parents.</summary>
    void SetValue(RegistryLocation location, RegistryValue value);

    /// <summary>Deletes the value. Does nothing when it is already absent.</summary>
    void DeleteValue(RegistryLocation location);

    bool KeyExists(RegistryRoot root, string keyPath);

    /// <summary>The names of the key's direct subkeys, or none when the key does not exist.</summary>
    IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string keyPath);

    /// <summary>Deletes the key only when it has no values and no subkeys left.</summary>
    void DeleteKeyIfEmpty(RegistryRoot root, string keyPath);
}
