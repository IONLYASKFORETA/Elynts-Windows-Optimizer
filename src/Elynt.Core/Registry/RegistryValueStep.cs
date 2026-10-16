using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Registry;

/// <summary>A tweak step that sets one registry value.</summary>
public sealed record RegistryValueStep(RegistryLocation Location, RegistryValue Desired) : TweakStep
{
    public override ChangeTarget Target => new RegistryTarget(Location, Desired);

    public override string DescribeChange() => $"Set {Location} to {Desired}";

    public override bool IsApplied(SystemAdapters system) => Desired.Equals(system.Registry.GetValue(Location));

    public override StepRecord Capture(SystemAdapters system)
    {
        return new RegistryValueRecord(Location, system.Registry.GetValue(Location), Desired, MissingKeys(system.Registry, Location));
    }

    public override void Apply(SystemAdapters system) => system.Registry.SetValue(Location, Desired);

    /// <summary>Every key a write to <paramref name="location"/> will create, deepest first, so undo removes exactly those.</summary>
    internal static IReadOnlyList<string> MissingKeys(IRegistry registry, RegistryLocation location)
    {
        var createdKeys = new List<string>();
        for (var path = location.KeyPath; path.Length > 0 && !registry.KeyExists(location.Root, path); path = Parent(path))
            createdKeys.Add(path);
        return createdKeys;
    }

    private static string Parent(string keyPath)
    {
        var slash = keyPath.LastIndexOf('\\');
        return slash < 0 ? "" : keyPath[..slash];
    }
}

/// <summary>The exact prior state of one registry value, captured before the app changed it.</summary>
/// <param name="Before">The value as it was, or null when it did not exist.</param>
/// <param name="After">What the app wrote.</param>
/// <param name="CreatedKeys">Keys that did not exist before, deepest first.</param>
public sealed record RegistryValueRecord(
    RegistryLocation Location,
    RegistryValue? Before,
    RegistryValue After,
    IReadOnlyList<string> CreatedKeys) : StepRecord
{
    public override void Revert(SystemAdapters system)
    {
        // A value that did not exist is deleted, never set to zero.
        if (Before is null)
            system.Registry.DeleteValue(Location);
        else
            system.Registry.SetValue(Location, Before);

        foreach (var key in CreatedKeys)
            system.Registry.DeleteKeyIfEmpty(Location.Root, key);
    }
}
