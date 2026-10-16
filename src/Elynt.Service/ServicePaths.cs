using System.Security.AccessControl;
using System.Security.Principal;

namespace Elynt.Service;

/// <summary>Where the service keeps its journal and logs: %ProgramData%\ElyntOptimizer.</summary>
internal static class ServicePaths
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ElyntOptimizer");

    public static string Journal { get; } = Path.Combine(Root, "journal", "journal.json");

    public static string Logs { get; } = Path.Combine(Root, "logs");

    /// <summary>Whole-store backups (BCD, firewall, power plans, tasks, drivers, hosts) taken before the first change of each kind.</summary>
    public static string Backups { get; } = Path.Combine(Root, "backups");

    /// <summary>One log per repair run, readable by users so the app can copy them.</summary>
    public static string RepairLogs { get; } = Path.Combine(Logs, "repair");

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
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(RepairLogs);
    }
}
