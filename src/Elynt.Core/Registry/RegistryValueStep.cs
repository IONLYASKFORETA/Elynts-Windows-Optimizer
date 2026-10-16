using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Registry;

/// <summary>A tweak step that sets one registry value.</summary>
public sealed record RegistryValueStep(RegistryLocation Location, RegistryValue Desired) : TweakStep
{
    public override string DescribeChange() => $"Set {Location} to {Desired}";

    public override bool IsApplied(SystemAdapters system) => Desired.Equals(system.Registry.GetValue(Location));

    public override StepRecord Capture(SystemAdapters system)
    {
        // Record every key this step will create, deepest first, so undo removes exactly those.
        var createdKeys = new List<string>();
        for (var path = Location.KeyPath; path.Length > 0 && !system.Registry.KeyExists(Location.Root, path); path = Parent(path))
            createdKeys.Add(path);

        return new RegistryValueRecord(Location, system.Registry.GetValue(Location), Desired, createdKeys);
    }

    public override void Apply(SystemAdapters system) => system.Registry.SetValue(Location, Desired);

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
    public override string? FindConflict(SystemAdapters system)
    {
        var now = system.Registry.GetValue(Location);
        if (After.Equals(now) || Equals(Before, now))
            return null;

        var shown = now?.ToString() ?? "deleted";
        return $"Something else changed {Location} since the app set it to {After}. It is now {shown}.";
    }

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
