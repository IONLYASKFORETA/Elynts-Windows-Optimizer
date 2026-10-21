using Elynt.Core.Power;

namespace Elynt.Core.Tests;

/// <summary>Power plans in memory, starting with the three Windows ships (Ultimate is a hidden template).</summary>
public sealed class FakePowerPlans : IPowerPlans
{
    private readonly List<Guid> _plans = [PowerPlans.Balanced, PowerPlans.HighPerformance, PowerPlans.PowerSaver];
    private readonly HashSet<Guid> _templates = [PowerPlans.UltimatePerformanceTemplate];

    public IReadOnlyList<Guid> Plans => _plans;

    public Guid Active { get; set; } = PowerPlans.Balanced;

    public void Remove(Guid plan) => _plans.Remove(plan);

    Guid IPowerPlans.GetActive() => Active;

    public void SetActive(Guid plan)
    {
        if (!_plans.Contains(plan))
            throw new InvalidOperationException($"There is no power plan {plan}.");
        Active = plan;
    }

    public bool Exists(Guid plan) => _plans.Contains(plan);

    public void Duplicate(Guid source, Guid newPlan)
    {
        if (!_plans.Contains(source) && !_templates.Contains(source))
            throw new InvalidOperationException($"There is no power plan {source}.");
        if (_plans.Contains(newPlan))
            throw new InvalidOperationException($"Power plan {newPlan} already exists.");
        _plans.Add(newPlan);
    }

    private readonly Dictionary<(Guid, Guid, Guid, PowerSource), uint> _settings = [];

    /// <summary>Settings never set read as 0.</summary>
    public uint GetSetting(Guid plan, Guid subgroup, Guid setting, PowerSource source)
    {
        if (!_plans.Contains(plan))
            throw new InvalidOperationException($"There is no power plan {plan}.");
        return _settings.GetValueOrDefault((plan, subgroup, setting, source));
    }

    public void SetSetting(Guid plan, Guid subgroup, Guid setting, PowerSource source, uint value)
    {
        if (!_plans.Contains(plan))
            throw new InvalidOperationException($"There is no power plan {plan}.");
        _settings[(plan, subgroup, setting, source)] = value;
    }

    public void Delete(Guid plan)
    {
        if (plan == Active)
            throw new InvalidOperationException("The active power plan can't be deleted.");
        _plans.Remove(plan);
    }
}
