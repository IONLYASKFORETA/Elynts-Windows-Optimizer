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

    /// <summary>
    /// Reads the value with powercfg /qh, which also shows hidden settings such as CPMINCORES. The output's labels are
    /// translated, so only its hex numbers are read: the last two are the current plugged-in and battery values.
    /// </summary>
    public uint GetSetting(Guid plan, Guid subgroup, Guid setting, PowerSource source)
    {
        var output = CommandLine.Run("powercfg.exe", "/qh", plan.ToString(), subgroup.ToString(), setting.ToString());
        var numbers = HexNumber().Matches(output);
        if (numbers.Count < 2)
            throw new InvalidOperationException($"powercfg didn't show power setting {setting} on plan {plan}.");
        var current = numbers[numbers.Count - (source == PowerSource.Ac ? 2 : 1)].Value;
        return Convert.ToUInt32(current[2..], 16);
    }

    public void SetSetting(Guid plan, Guid subgroup, Guid setting, PowerSource source, uint value)
    {
        var command = source == PowerSource.Ac ? "/setacvalueindex" : "/setdcvalueindex";
        CommandLine.Run("powercfg.exe", command, plan.ToString(), subgroup.ToString(), setting.ToString(), value.ToString());
        // Changes to the active plan only take effect once it is made active again.
        if (GetActive() == plan)
            SetActive(plan);
    }

    [GeneratedRegex(@"0x[0-9a-fA-F]{8}\b")]
    private static partial Regex HexNumber();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex PlanGuid();
}
