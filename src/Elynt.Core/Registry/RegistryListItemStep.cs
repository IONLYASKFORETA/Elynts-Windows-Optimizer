using System.Text;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Registry;

/// <summary>How a REG_SZ that holds several settings is laid out.</summary>
public enum RegistryListFormat
{
    /// <summary><c>Name=Value;Name=Value;</c>, as DirectX keeps its per-user graphics settings.</summary>
    SemicolonPairs,

    /// <summary><c>~ FLAG FLAG</c>, as the Compatibility tab keeps its per-program flags under AppCompatFlags\Layers.</summary>
    CompatibilityLayers,
}

/// <summary>
/// A tweak step that sets one item inside a REG_SZ list of settings, leaving the other items as they are.
/// For <see cref="RegistryListFormat.CompatibilityLayers"/> the item is a bare flag and <see cref="Value"/> is null.
/// </summary>
public sealed record RegistryListItemStep(RegistryLocation Location, RegistryListFormat Format, string Item, string? Value)
    : TweakStep
{
    public override ChangeTarget Target => new RegistryTarget(Location, RegistryValue.String(ItemText));

    private string ItemText => Value is null ? Item : $"{Item}={Value}";

    public override string DescribeChange() => Format == RegistryListFormat.SemicolonPairs
        ? $"Set {ItemText} inside {Location} (REG_SZ), keeping the other settings in it"
        : $"Add {Item} to {Location} (REG_SZ), keeping any other flags in it";

    public override bool IsApplied(SystemAdapters system) =>
        RegistryList.Parse(Format, system.Registry.GetValue(Location)).Get(Item) is { } found && found.Value == Value;

    public override StepRecord Capture(SystemAdapters system)
    {
        var before = system.Registry.GetValue(Location);
        var item = RegistryList.Parse(Format, before).Get(Item);
        return new RegistryListItemRecord(
            Location, Format, Item, before, Desired(before), item is not null, item?.Value,
            RegistryValueStep.MissingKeys(system.Registry, Location));
    }

    public override void Apply(SystemAdapters system) =>
        system.Registry.SetValue(Location, Desired(system.Registry.GetValue(Location)));

    private RegistryValue Desired(RegistryValue? current) =>
        RegistryList.Parse(Format, current).With(Item, Value).ToValue();
}

/// <summary>The exact prior state of a list value, plus the one item the app changed in it.</summary>
/// <param name="Before">The whole value as it was, or null when it did not exist.</param>
/// <param name="After">The whole value the app wrote.</param>
/// <param name="ItemExisted">Whether the item was in the list before.</param>
/// <param name="ItemValueBefore">The item's value before, for <c>Name=Value</c> lists.</param>
public sealed record RegistryListItemRecord(
    RegistryLocation Location,
    RegistryListFormat Format,
    string Item,
    RegistryValue? Before,
    RegistryValue After,
    bool ItemExisted,
    string? ItemValueBefore,
    IReadOnlyList<string> CreatedKeys) : StepRecord
{
    /// <summary>
    /// Only the app's own item counts: other items in the list (usually changed in Windows Settings) are kept on undo,
    /// so changing them is not a conflict.
    /// </summary>
    public override string? FindConflict(SystemAdapters system)
    {
        var item = RegistryList.Parse(Format, system.Registry.GetValue(Location)).Get(Item);
        var written = RegistryList.Parse(Format, After).Get(Item);
        if (item is not null && item.Value == written?.Value)
            return null;
        if (ItemExisted ? item is not null && item.Value == ItemValueBefore : item is null)
            return null;

        var shown = item is null ? "removed" : item.Value is null ? item.Item : $"{item.Item}={item.Value}";
        return $"Something else changed {Item} in {Location} since the app set it. It is now {shown}.";
    }

    public override void Revert(SystemAdapters system)
    {
        var current = system.Registry.GetValue(Location);
        RegistryValue? restored;
        if (After.Equals(current))
        {
            // Nothing else touched the list since: put back the exact bytes, or delete a value that didn't exist.
            restored = Before;
        }
        else
        {
            // Something else (usually Windows Settings) changed other items since. Keep those; undo only our item.
            var list = RegistryList.Parse(Format, current);
            list = ItemExisted ? list.With(Item, ItemValueBefore) : list.Without(Item);
            restored = list.IsEmpty ? null : list.ToValue();
        }

        if (restored is null)
            system.Registry.DeleteValue(Location);
        else
            system.Registry.SetValue(Location, restored);

        foreach (var key in CreatedKeys)
            system.Registry.DeleteKeyIfEmpty(Location.Root, key);
    }
}

/// <summary>A parsed list value. A value that isn't a string is treated as an empty list and replaced when written.</summary>
internal sealed record RegistryList(RegistryListFormat Format, IReadOnlyList<RegistryList.Entry> Items)
{
    internal sealed record Entry(string Item, string? Value);

    public bool IsEmpty => Items.Count == 0;

    public Entry? Get(string item) =>
        Items.FirstOrDefault(e => e.Item.Equals(item, StringComparison.OrdinalIgnoreCase));

    /// <summary>Sets the item in place, or adds it at the end.</summary>
    public RegistryList With(string item, string? value)
    {
        var entry = new Entry(item, value);
        return Get(item) is null
            ? this with { Items = [.. Items, entry] }
            : this with { Items = Items.Select(e => e.Item.Equals(item, StringComparison.OrdinalIgnoreCase) ? entry : e).ToList() };
    }

    public RegistryList Without(string item) =>
        this with { Items = Items.Where(e => !e.Item.Equals(item, StringComparison.OrdinalIgnoreCase)).ToList() };

    public static RegistryList Parse(RegistryListFormat format, RegistryValue? value)
    {
        if (value is not { Kind: RegistryValueKind.String or RegistryValueKind.ExpandString })
            return new(format, []);
        var text = Encoding.Unicode.GetString(value.Data).Split('\0')[0];
        var items = format == RegistryListFormat.SemicolonPairs
            ? text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(pair => pair.Split('=', 2))
                .Select(parts => new Entry(parts[0], parts.Length > 1 ? parts[1] : null))
            : text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token != "~")
                .Select(token => new Entry(token, null));
        return new(format, items.ToList());
    }

    public RegistryValue ToValue() => RegistryValue.String(Format == RegistryListFormat.SemicolonPairs
        ? string.Concat(Items.Select(e => e.Value is null ? $"{e.Item};" : $"{e.Item}={e.Value};"))
        // "~" marks flags set by the user rather than by the compatibility database; Windows writes it first.
        : string.Join(' ', Items.Select(e => e.Item).Prepend("~")));
}
