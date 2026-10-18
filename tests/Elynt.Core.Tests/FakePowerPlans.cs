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

    public void Delete(Guid plan)
    {
        if (plan == Active)
            throw new InvalidOperationException("The active power plan can't be deleted.");
        _plans.Remove(plan);
    }
}
