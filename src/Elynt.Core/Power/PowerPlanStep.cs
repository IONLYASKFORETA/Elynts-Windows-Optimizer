using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Power;

/// <summary>Windows power plans (powercfg).</summary>
public interface IPowerPlans
{
    Guid GetActive();

    void SetActive(Guid plan);

    /// <summary>Whether the plan is one of this PC's plans (hidden templates don't count).</summary>
    bool Exists(Guid plan);

    /// <summary>Creates <paramref name="newPlan"/> as a copy of <paramref name="source"/>, which may be a hidden template.</summary>
    void Duplicate(Guid source, Guid newPlan);

    void Delete(Guid plan);

    /// <summary>A setting's value index on <paramref name="plan"/> (powercfg /query).</summary>
    uint GetSetting(Guid plan, Guid subgroup, Guid setting, PowerSource source);

    /// <summary>Sets a setting's value index on <paramref name="plan"/>; takes effect at once when the plan is active.</summary>
    void SetSetting(Guid plan, Guid subgroup, Guid setting, PowerSource source, uint value);
}

/// <summary>Well-known power plan GUIDs.</summary>
public static class PowerPlans
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    /// <summary>The hidden Ultimate Performance template; it has to be copied before it can be used.</summary>
    public static readonly Guid UltimatePerformanceTemplate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    /// <summary>The app's own copy of Ultimate Performance, so undo knows exactly which plan it created.</summary>
    public static readonly Guid ElyntUltimatePerformance = new("4c1e7a3b-6d2f-4e8a-9b5c-e1a7d0c3b201");
}

/// <summary>
/// A tweak step that makes <paramref name="Plan"/> the active power plan, first creating it from
/// <paramref name="CreateFrom"/> when it doesn't exist yet.
/// </summary>
public sealed record PowerPlanStep(Guid Plan, string PlanName, Guid? CreateFrom = null) : TweakStep
{
    public override ChangeTarget Target => new PowerPlanTarget(Plan);

    public override Safety.BackupStore? Store => Safety.BackupStore.PowerPlans;

    public override string DescribeChange() => CreateFrom is { } source
        ? $"Create the {PlanName} plan as {Plan} (powercfg /duplicatescheme {source} {Plan}) if it isn't there, then make it active (powercfg /setactive {Plan})"
        : $"Make {PlanName} the active power plan (powercfg /setactive {Plan})";

    public override bool IsApplied(SystemAdapters system) => Power(system).GetActive() == Plan;

    public override StepRecord Capture(SystemAdapters system)
    {
        var power = Power(system);
        return new PowerPlanRecord(power.GetActive(), Plan, Created: CreateFrom is not null && !power.Exists(Plan));
    }

    public override void Apply(SystemAdapters system)
    {
        var power = Power(system);
        if (!power.Exists(Plan))
        {
            if (CreateFrom is not { } source)
                throw new InvalidOperationException($"This PC doesn't offer the {PlanName} power plan.");
            power.Duplicate(source, Plan);
        }
        power.SetActive(Plan);
    }

    internal static IPowerPlans Power(SystemAdapters system) =>
        system.Power ?? throw new InvalidOperationException("Power plans can't be read on this PC.");
}

/// <summary>The plan that was active before, and whether the app created <paramref name="Plan"/>.</summary>
public sealed record PowerPlanRecord(Guid Before, Guid Plan, bool Created) : StepRecord
{
    /// <summary>The active power plan is the PC's, whoever is signed in.</summary>
    public override bool AffectsEveryAccount() => true;

    public override string? FindConflict(SystemAdapters system)
    {
        var active = PowerPlanStep.Power(system).GetActive();
        return active == Plan || active == Before
            ? null
            : $"Something else switched the power plan since the app made {Plan} active. The active plan is now {active}.";
    }

    public override void Revert(SystemAdapters system)
    {
        var power = PowerPlanStep.Power(system);
        power.SetActive(Before);
        if (Created && power.Exists(Plan))
            power.Delete(Plan);
    }
}
