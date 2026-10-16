using System.Text.RegularExpressions;
using Elynt.Core.Power;

namespace Elynt.Windows;

/// <summary>Power plans through powercfg. Only the GUIDs in its output are read, so the language doesn't matter.</summary>
public sealed partial class WindowsPowerPlans : IPowerPlans
{
    public Guid GetActive() =>
        Guid.Parse(PlanGuid().Match(CommandLine.Run("powercfg.exe", "/getactivescheme")).Value);

    public void SetActive(Guid plan) => CommandLine.Run("powercfg.exe", "/setactive", plan.ToString());

    public bool Exists(Guid plan) =>
        PlanGuid().Matches(CommandLine.Run("powercfg.exe", "/list")).Any(m => Guid.Parse(m.Value) == plan);

    public void Duplicate(Guid source, Guid newPlan) =>
        CommandLine.Run("powercfg.exe", "/duplicatescheme", source.ToString(), newPlan.ToString());

    public void Delete(Guid plan) => CommandLine.Run("powercfg.exe", "/delete", plan.ToString());

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex PlanGuid();
}
