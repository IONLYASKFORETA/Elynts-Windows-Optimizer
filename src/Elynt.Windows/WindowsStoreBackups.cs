using System.Text.RegularExpressions;
using Elynt.Core.Safety;

namespace Elynt.Windows;

/// <summary>
/// Whole-store backups with Windows' own export tools, each into its own time-stamped folder under
/// <paramref name="root"/>. These are the recovery path if the journal itself is ever lost.
/// </summary>
public sealed partial class WindowsStoreBackups(string root, TimeProvider? clock = null) : IStoreBackups
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public string Export(BackupStore store)
    {
        var folder = Path.Combine(root, $"{_clock.GetUtcNow():yyyyMMdd-HHmmss}-{store}");
        Directory.CreateDirectory(folder);

        switch (store)
        {
            case BackupStore.BootConfiguration:
                SystemCommand.Run("bcdedit.exe", ["/export", Path.Combine(folder, "bcd.bak")]);
                break;
            case BackupStore.Firewall:
                SystemCommand.Run("netsh.exe", ["advfirewall", "export", Path.Combine(folder, "firewall.wfw")]);
                break;
            case BackupStore.PowerPlans:
                ExportPowerPlans(folder);
                break;
            case BackupStore.ScheduledTasks:
                // "/xml ONE" writes every task's definition as one XML document.
                File.WriteAllText(Path.Combine(folder, "tasks.xml"), SystemCommand.Run("schtasks.exe", ["/query", "/xml", "ONE"]));
                break;
            case BackupStore.Drivers:
                var drivers = Path.Combine(folder, "drivers");
                Directory.CreateDirectory(drivers);
                SystemCommand.Run("pnputil.exe", ["/export-driver", "*", drivers], TimeSpan.FromMinutes(15));
                break;
            case BackupStore.Hosts:
                File.Copy(Path.Combine(SystemCommand.System32, "drivers", "etc", "hosts"), Path.Combine(folder, "hosts"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(store), store, null);
        }
        return folder;
    }

    /// <summary>Exports every plan, and notes which one is active so it can be switched back to.</summary>
    private static void ExportPowerPlans(string folder)
    {
        var list = SystemCommand.Run("powercfg.exe", ["/list"]);
        foreach (Match plan in PlanGuid().Matches(list))
        {
            var guid = plan.Groups[1].Value;
            SystemCommand.Run("powercfg.exe", ["/export", Path.Combine(folder, $"{guid}.pow"), guid]);
        }
        File.WriteAllText(Path.Combine(folder, "plans.txt"), list);
        File.WriteAllText(Path.Combine(folder, "active.txt"), SystemCommand.Run("powercfg.exe", ["/getactivescheme"]));
    }

    [GeneratedRegex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})")]
    private static partial Regex PlanGuid();
}
