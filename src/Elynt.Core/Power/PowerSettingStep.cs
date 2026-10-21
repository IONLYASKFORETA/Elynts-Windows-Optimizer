using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Power;

/// <summary>Whether a power plan setting applies plugged in (AC) or on battery (DC).</summary>
public enum PowerSource
{
    Ac,
    Dc,
}

/// <summary>Well-known power setting GUIDs (powercfg aliases in comments).</summary>
public static class PowerSettings
{
    /// <summary>SUB_PROCESSOR.</summary>
    public static readonly Guid ProcessorSubgroup = new("54533251-82be-4824-96c1-47b60b740d00");

    /// <summary>CPMINCORES: the lowest share of cores, in percent, core parking keeps unparked.</summary>
    public static readonly Guid CoreParkingMinCores = new("0cc5b647-c1df-4637-891a-dec35c318583");
}

/// <summary>
/// A tweak step that sets one power setting on the active plan, for both plugged in and on battery. Undo puts the
/// values back on that same plan, even if another plan is active by then.
/// </summary>
public sealed record PowerSettingStep(Guid Subgroup, Guid Setting, string SettingName, uint Value) : TweakStep
{
    public override ChangeTarget Target => new PowerSettingTarget(Subgroup, Setting, Value);

    public override Safety.BackupStore? Store => Safety.BackupStore.PowerPlans;

    public override string DescribeChange() =>
        $"Set {SettingName} to {Value} on the active power plan, plugged in and on battery " +
        $"(powercfg /setacvalueindex and /setdcvalueindex SCHEME_CURRENT {Subgroup} {Setting} {Value})";

    public override bool IsApplied(SystemAdapters system)
    {
        var power = PowerPlanStep.Power(system);
        var plan = power.GetActive();
        return power.GetSetting(plan, Subgroup, Setting, PowerSource.Ac) == Value &&
               power.GetSetting(plan, Subgroup, Setting, PowerSource.Dc) == Value;
    }

    public override StepRecord Capture(SystemAdapters system)
    {
        var power = PowerPlanStep.Power(system);
        var plan = power.GetActive();
        return new PowerSettingRecord(
            plan, Subgroup, Setting, SettingName,
            power.GetSetting(plan, Subgroup, Setting, PowerSource.Ac),
            power.GetSetting(plan, Subgroup, Setting, PowerSource.Dc),
            Value);
    }

    public override void Apply(SystemAdapters system)
    {
        var power = PowerPlanStep.Power(system);
        var plan = power.GetActive();
        power.SetSetting(plan, Subgroup, Setting, PowerSource.Ac, Value);
        power.SetSetting(plan, Subgroup, Setting, PowerSource.Dc, Value);
    }
}

/// <summary>A power setting's values on <paramref name="Plan"/> before the app changed them.</summary>
public sealed record PowerSettingRecord(
    Guid Plan, Guid Subgroup, Guid Setting, string SettingName, uint BeforeAc, uint BeforeDc, uint After) : StepRecord
{
    public override string? FindConflict(SystemAdapters system)
    {
        var power = PowerPlanStep.Power(system);
        if (!power.Exists(Plan))
            return $"The power plan {Plan} that the app changed {SettingName} on has been deleted since.";
        var ac = power.GetSetting(Plan, Subgroup, Setting, PowerSource.Ac);
        var dc = power.GetSetting(Plan, Subgroup, Setting, PowerSource.Dc);
        return (ac == After || ac == BeforeAc) && (dc == After || dc == BeforeDc)
            ? null
            : $"Something else changed {SettingName} on power plan {Plan} since the app set it to {After}. " +
              $"It is now {ac} plugged in and {dc} on battery.";
    }

    public override void Revert(SystemAdapters system)
    {
        var power = PowerPlanStep.Power(system);
        power.SetSetting(Plan, Subgroup, Setting, PowerSource.Ac, BeforeAc);
        power.SetSetting(Plan, Subgroup, Setting, PowerSource.Dc, BeforeDc);
    }
}
