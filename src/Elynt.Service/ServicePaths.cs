using System.Security.AccessControl;
using System.Security.Principal;

namespace Elynt.Service;

/// <summary>Where the service keeps its journal and logs: %ProgramData%\ElyntOptimizer.</summary>
internal static class ServicePaths
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ElyntOptimizer");

    public static string Journal { get; } = Path.Combine(Root, "journal", "journal.json");

    /// <summary>Each user's weekly cleaning choice and when it last ran.</summary>
    public static string CleanupSchedule { get; } = Path.Combine(Root, "cleanup-schedule.json");

    /// <summary>Each user's drift state: the Windows build last checked, automatic re-apply, and changes not supported on this build.</summary>
    public static string Drift { get; } = Path.Combine(Root, "drift.json");

    /// <summary>Crash-safe GPU and CPU tuning state: what is tuned, what was confirmed, and whether the last stop was clean.</summary>
    public static string Tuning { get; } = Path.Combine(Root, "tuning", "tuning.json");

    public static string Logs { get; } = Path.Combine(Root, "logs");

    /// <summary>The startup items each user has already been told about, so only new ones raise a tray note.</summary>
    public static string KnownStartupItems { get; } = Path.Combine(Root, "startup", "known.json");

    /// <summary>Whole-store backups (BCD, firewall, power plans, tasks, drivers, hosts) taken before the first change of each kind.</summary>
    public static string Backups { get; } = Path.Combine(Root, "backups");

    /// <summary>One log per repair run, readable by users so the app can copy them.</summary>
    public static string RepairLogs { get; } = Path.Combine(Logs, "repair");

    /// <summary>Driver installers, between download and install. Users can't write here, so a verified file can't be swapped.</summary>
    public static string Downloads { get; } = Path.Combine(Root, "downloads");

    /// <summary>Games each user added by hand, one file per user SID.</summary>
    public static string Games { get; } = Path.Combine(Root, "games");

    /// <summary>
    /// Creates the folders. Only SYSTEM and Administrators may write the journal; users may read it and the logs
    /// (so the app can offer "copy log"), but cannot plant or edit files there.
    /// </summary>
    public static void EnsureCreated()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));

        var root = new DirectoryInfo(Root);
        if (!root.Exists)
            root.Create(security);
        else
            root.SetAccessControl(security);

        Directory.CreateDirectory(Path.GetDirectoryName(Journal)!);
        Directory.CreateDirectory(Path.GetDirectoryName(Tuning)!);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Path.GetDirectoryName(KnownStartupItems)!);
        Directory.CreateDirectory(RepairLogs);
        Directory.CreateDirectory(Downloads);
        Directory.CreateDirectory(Games);
    }
}
