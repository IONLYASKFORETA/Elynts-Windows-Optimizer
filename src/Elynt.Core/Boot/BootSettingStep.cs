using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Boot;

/// <summary>The settings of the boot entry Windows started from (bcdedit's {current}).</summary>
public interface IBootConfig
{
    /// <summary>The element's value as bcdedit shows it (e.g. "Yes"), or null when it isn't set.</summary>
    string? Get(string element);

    void Set(string element, string value);

    /// <summary>Removes the element, so Windows uses its default. Does nothing when it isn't set.</summary>
    void Delete(string element);
}

/// <summary>A tweak step that sets one boot setting, or removes it when <paramref name="Value"/> is null.</summary>
public sealed record BootSettingStep(string Element, string? Value) : TweakStep
{
    public override ChangeTarget Target => new BootSettingTarget(Element, Value);

    public override string DescribeChange() => Value is null
        ? $"Remove the boot setting {Element} (bcdedit /deletevalue {{current}} {Element})"
        : $"Set the boot setting {Element} to {Value} (bcdedit /set {{current}} {Element} {Value})";

    public override bool IsApplied(SystemAdapters system)
    {
        var current = Boot(system).Get(Element);
        return Value is null ? current is null : string.Equals(current, Value, StringComparison.OrdinalIgnoreCase);
    }

    public override StepRecord Capture(SystemAdapters system) => new BootSettingRecord(Element, Boot(system).Get(Element));

    public override void Apply(SystemAdapters system)
    {
        if (Value is null)
            Boot(system).Delete(Element);
        else
            Boot(system).Set(Element, Value);
    }

    internal static IBootConfig Boot(SystemAdapters system) =>
        system.Boot ?? throw new InvalidOperationException("Boot settings can't be read on this PC.");
}

/// <summary>A boot setting as it was before the app changed it: its value, or null when it wasn't set.</summary>
public sealed record BootSettingRecord(string Element, string? Before) : StepRecord
{
    public override void Revert(SystemAdapters system)
    {
        var boot = BootSettingStep.Boot(system);
        if (Before is null)
            boot.Delete(Element);
        else
            boot.Set(Element, Before);
    }
}
