using System.Globalization;
using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;

namespace Elynt.Core.UpdateControl;

/// <summary>
/// Writes one of the Settings app's own pause values, dated from today by the PC's clock: a start value is today,
/// an end value is <paramref name="Days"/> later. Days are whole UTC days, so the change captured before applying
/// is the change applied. Windows allows at most 35 days, and so do the hard rules.
/// </summary>
public sealed record UpdatePauseStep(string ValueName, bool IsEnd, int Days) : TweakStep
{
    /// <summary>Windows' own longest pause (Settings &gt; Windows Update &gt; Pause updates).</summary>
    public const int MaxDays = 35;

    internal const string UxSettings = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

    private const string Format = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public RegistryLocation Location => new(RegistryRoot.LocalMachine, UxSettings, ValueName);

    public override ChangeTarget Target => new UpdatePauseTarget(ValueName, Days);

    public override string DescribeChange() => IsEnd
        ? $"Set {Location} to today plus {Days} days"
        : $"Set {Location} to today";

    /// <summary>A start value is in place while it holds a date; an end value only while that date is still ahead.</summary>
    public override bool IsApplied(SystemAdapters system)
    {
        if (system.Registry.GetValue(Location) is not { Kind: RegistryValueKind.String } value ||
            !DateTimeOffset.TryParse(System.Text.Encoding.Unicode.GetString(value.Data).TrimEnd('\0'), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            return false;
        return !IsEnd || date > system.Clock.GetUtcNow();
    }

    public override StepRecord Capture(SystemAdapters system) =>
        new RegistryValueRecord(Location, system.Registry.GetValue(Location), Desired(system),
            RegistryValueStep.MissingKeys(system.Registry, Location));

    public override void Apply(SystemAdapters system) => system.Registry.SetValue(Location, Desired(system));

    private RegistryValue Desired(SystemAdapters system)
    {
        var today = new DateTimeOffset(system.Clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        var date = IsEnd ? today.AddDays(Days) : today;
        return RegistryValue.String(date.ToString(Format, CultureInfo.InvariantCulture));
    }
}
