using System.Security;
using System.Text;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Registry;

/// <summary>
/// Picks which of the keys matching a pattern a <see cref="RegistryEachKeyStep"/> changes: those with a value named
/// <paramref name="ValueName"/>, and when <paramref name="EqualsText"/> is set, only where that REG_SZ reads the same
/// (ignoring case).
/// </summary>
public sealed record RegistryKeyFilter(string ValueName, string? EqualsText = null)
{
    public bool Matches(IRegistry registry, RegistryRoot root, string keyPath)
    {
        var value = registry.GetValue(new(root, keyPath, ValueName));
        if (value is null)
            return false;
        if (EqualsText is null)
            return true;
        return value.Kind is RegistryValueKind.String or RegistryValueKind.ExpandString &&
               string.Equals(Encoding.Unicode.GetString(value.Data).TrimEnd('\0'), EqualsText, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => EqualsText is null ? $"that have {ValueName}" : $"whose {ValueName} is {EqualsText}";
}

/// <summary>
/// A tweak step that sets one registry value under every key matching <paramref name="KeyPattern"/>, for settings
/// Windows keeps once per device or per network interface. A <c>*</c> path segment matches any one subkey. The
/// value goes in <paramref name="SubPath"/> under each matched key when it is set. A PC with no matching keys has
/// nothing to change, so the step counts as applied there.
/// </summary>
public sealed record RegistryEachKeyStep(
    RegistryRoot Root,
    string KeyPattern,
    RegistryKeyFilter? Filter,
    string? SubPath,
    string ValueName,
    RegistryValue Desired) : TweakStep
{
    private string FullPattern => SubPath is null ? KeyPattern : $@"{KeyPattern}\{SubPath}";

    public override ChangeTarget Target => new RegistryPatternTarget(new(Root, FullPattern, ValueName), Desired);

    public override string DescribeChange()
    {
        var where = Filter is null ? "" : $" {Filter}";
        return $"Set {new RegistryLocation(Root, FullPattern, ValueName)} to {Desired}, for every key matching {KeyPattern}{where}";
    }

    /// <summary>The value locations this step changes on this PC.</summary>
    public IReadOnlyList<RegistryLocation> Matches(IRegistry registry)
    {
        IEnumerable<string> keys = [""];
        foreach (var segment in KeyPattern.Split('\\'))
        {
            keys = segment == "*"
                ? keys.SelectMany(key => SubKeys(registry, key).Select(name => Join(key, name)))
                : keys.Select(key => Join(key, segment));
        }

        return keys
            .Where(key => Readable(() => registry.KeyExists(Root, key) && (Filter is null || Filter.Matches(registry, Root, key))))
            .Select(key => new RegistryLocation(Root, SubPath is null ? key : Join(key, SubPath), ValueName))
            .ToList();
    }

    private IReadOnlyList<string> SubKeys(IRegistry registry, string key)
    {
        try
        {
            return registry.GetSubKeyNames(Root, key);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Keys Windows locks even from administrators (e.g. a device class's Properties key) are skipped.</summary>
    private static bool Readable(Func<bool> check)
    {
        try
        {
            return check();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Join(string key, string name) => key.Length == 0 ? name : $@"{key}\{name}";

    public override bool IsApplied(SystemAdapters system) =>
        Matches(system.Registry).All(location => Desired.Equals(system.Registry.GetValue(location)));

    public override StepRecord Capture(SystemAdapters system) => new RegistryEachKeyRecord(
        Matches(system.Registry)
            .Select(location => new RegistryValueRecord(
                location, system.Registry.GetValue(location), Desired, RegistryValueStep.MissingKeys(system.Registry, location)))
            .ToList());

    public override void Apply(SystemAdapters system)
    {
        foreach (var location in Matches(system.Registry))
            system.Registry.SetValue(location, Desired);
    }
}

/// <summary>The exact prior state of each value a <see cref="RegistryEachKeyStep"/> changed.</summary>
public sealed record RegistryEachKeyRecord(IReadOnlyList<RegistryValueRecord> Values) : StepRecord
{
    /// <summary>
    /// Whole-PC when every value it changed is under HKLM. One that matched no keys changed nothing in any account's
    /// own settings, so it counts as whole-PC too.
    /// </summary>
    public override bool AffectsEveryAccount() => Values.All(value => value.AffectsEveryAccount());

    public override string? FindConflict(SystemAdapters system) =>
        Values.Select(value => value.FindConflict(system)).FirstOrDefault(conflict => conflict is not null);

    public override void Revert(SystemAdapters system)
    {
        foreach (var value in Values)
            value.Revert(system);
    }
}
