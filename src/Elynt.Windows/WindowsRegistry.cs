using System.ComponentModel;
using System.Runtime.InteropServices;
using Elynt.Core.Registry;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using RegistryValueKind = Elynt.Core.Registry.RegistryValueKind;

namespace Elynt.Windows;

/// <summary>
/// The real registry. Values are read and written as raw bytes with their exact type, so undo restores precisely
/// what was there.
/// </summary>
/// <param name="userSid">
/// Whose settings <see cref="RegistryRoot.CurrentUser"/> means. The service passes the SID of the user who sent the
/// command (HKEY_USERS\&lt;sid&gt;), since its own HKEY_CURRENT_USER is the SYSTEM account's. Null uses this
/// process's own HKEY_CURRENT_USER.
/// </param>
public sealed partial class WindowsRegistry(string? userSid = null) : IRegistry
{
    private const int ErrorSuccess = 0;
    private const int ErrorFileNotFound = 2;
    private const int ErrorMoreData = 234;

    public RegistryValue? GetValue(RegistryLocation location)
    {
        using var key = OpenKey(location.Root, location.KeyPath, writable: false);
        if (key is null)
            return null;

        uint size = 0;
        var result = RegQueryValueEx(key.Handle, location.ValueName, 0, out _, null, ref size);
        while (true)
        {
            if (result == ErrorFileNotFound)
                return null;
            if (result != ErrorSuccess && result != ErrorMoreData)
                throw new Win32Exception(result, $"Couldn't read {location}.");

            // The value can grow between calls, so read until the buffer is big enough.
            var data = new byte[size];
            result = RegQueryValueEx(key.Handle, location.ValueName, 0, out var type, data, ref size);
            if (result == ErrorSuccess)
                return new RegistryValue((RegistryValueKind)type, data[..(int)size]);
        }
    }

    public void SetValue(RegistryLocation location, RegistryValue value)
    {
        using var root = OpenRoot(location.Root);
        using var key = root.CreateSubKey(location.KeyPath, writable: true);
        var result = RegSetValueEx(key.Handle, location.ValueName, 0, (uint)value.Kind, value.Data, (uint)value.Data.Length);
        if (result != ErrorSuccess)
            throw new Win32Exception(result, $"Couldn't write {location}.");
    }

    public void DeleteValue(RegistryLocation location)
    {
        using var key = OpenKey(location.Root, location.KeyPath, writable: true);
        key?.DeleteValue(location.ValueName, throwOnMissingValue: false);
    }

    public bool KeyExists(RegistryRoot root, string keyPath)
    {
        using var key = OpenKey(root, keyPath, writable: false);
        return key is not null;
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string keyPath)
    {
        using var key = OpenKey(root, keyPath, writable: false);
        return key?.GetSubKeyNames() ?? [];
    }

    public void DeleteKeyIfEmpty(RegistryRoot root, string keyPath)
    {
        using (var key = OpenKey(root, keyPath, writable: false))
        {
            if (key is null || key.ValueCount > 0 || key.SubKeyCount > 0)
                return;
        }

        using var rootKey = OpenRoot(root);
        rootKey.DeleteSubKey(keyPath, throwOnMissingSubKey: false);
    }

    private RegistryKey? OpenKey(RegistryRoot root, string keyPath, bool writable)
    {
        using var rootKey = OpenRoot(root);
        return rootKey.OpenSubKey(keyPath, writable);
    }

    private RegistryKey OpenRoot(RegistryRoot root)
    {
        switch (root)
        {
            case RegistryRoot.LocalMachine:
                return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            case RegistryRoot.CurrentUser when userSid is null:
                return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            case RegistryRoot.CurrentUser:
                using (var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64))
                {
                    return users.OpenSubKey(userSid, writable: true)
                        ?? throw new InvalidOperationException($"The settings of user {userSid} aren't loaded. Are they signed in?");
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(root), root, null);
        }
    }

    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryValueExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegQueryValueEx(SafeRegistryHandle key, string valueName, nint reserved, out uint type, byte[]? data, ref uint dataSize);

    [LibraryImport("advapi32.dll", EntryPoint = "RegSetValueExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegSetValueEx(SafeRegistryHandle key, string valueName, int reserved, uint type, byte[] data, uint dataSize);
}
