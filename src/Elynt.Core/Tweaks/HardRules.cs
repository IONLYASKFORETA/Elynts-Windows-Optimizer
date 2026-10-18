using System.Text.RegularExpressions;
using Elynt.Core.Registry;

namespace Elynt.Core.Tweaks;

/// <summary>
/// The never-touch rules from the spec. The Engine checks every step against them before applying, in every mode
/// including Advanced. Sources: research/anti-cheat-requirements.md (rows 7–9, 15–17) and the spec's "Hard rules".
/// </summary>
public static partial class HardRules
{
    /// <summary>BCD elements that weaken driver signing or turn on kernel debugging. Anti-cheats refuse to launch.</summary>
    private static readonly HashSet<string> ForbiddenBootElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "testsigning", "nointegritychecks", "debug", "bootdebug", "debugtype", "debugport", "hypervisordebug",
    };

    /// <summary>Known-vulnerable or generic port/physical-memory drivers (CVE-2020-14979, CVE-2019-16098 and kin).</summary>
    private static readonly HashSet<string> VulnerableDrivers = new(StringComparer.OrdinalIgnoreCase)
    {
        "WinRing0.sys", "WinRing0x64.sys", "inpout32.sys", "inpoutx64.sys", "RTCore32.sys", "RTCore64.sys",
        "gdrv.sys", "DBUtil_2_3.sys", "AsIO.sys", "AsIO2.sys", "AsIO3.sys", "physmem.sys", "PhyMemx64.sys",
    };

    /// <summary>
    /// Services the app never changes, even in Advanced: anti-cheats, Windows Update core and boot-critical drivers.
    /// </summary>
    public static IReadOnlySet<string> ProtectedServices { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Anti-cheat
        "vgc", "vgk", "FACEIT", "FACEITService", "BEService", "BEDaisy", "EasyAntiCheat", "EasyAntiCheat_EOS",
        // Windows Update core
        "wuauserv", "UsoSvc", "WaaSMedicSvc", "TrustedInstaller", "BITS", "CryptSvc",
        // Boot-critical drivers
        "ACPI", "pci", "partmgr", "disk", "volmgr", "volmgrx", "volsnap", "mountmgr", "fvevol", "Ntfs", "storahci",
        "stornvme", "iaStorVD", "storport", "CNG", "ksecdd", "Wdf01000", "WdFilter", "WdBoot", "CI", "msrpc", "fltmgr",
    };

    private static readonly string[] UserFolders =
    [
        "Documents", "Downloads", "Desktop", "Pictures", "Music", "Videos", "Saved Games", "Favorites", "Contacts",
    ];

    /// <summary>Why this change is never allowed, or null when it may go ahead.</summary>
    public static string? Violation(ChangeTarget target) => target switch
    {
        BootSettingTarget boot => BootViolation(boot),
        RegistryTarget registry => RegistryViolation(registry),
        ServiceTarget service when ProtectedServices.Contains(service.ServiceName) =>
            $"{service.ServiceName} is a protected service (anti-cheat, Windows Update or boot-critical)",
        DriverTarget driver when VulnerableDrivers.Contains(Path.GetFileName(driver.DriverFile.Replace('\\', '/'))) =>
            $"{driver.DriverFile} is a known-vulnerable driver that anti-cheats and Windows block",
        FileTarget file when IsUserFolder(file.Path) => "user folders and game saves are never touched",
        PackageTarget package when IsEdge(package.PackageName) => "Edge and WebView2 are never removed",
        _ => null,
    };

    private static string? BootViolation(BootSettingTarget boot)
    {
        if (ForbiddenBootElements.Contains(boot.Element) && boot.Value is not null)
            return $"the boot setting {boot.Element} turns off driver signing or turns on kernel debugging";
        if (boot.Element.Equals("loadoptions", StringComparison.OrdinalIgnoreCase) &&
            boot.Value?.Contains("INTEGRITY_CHECKS", StringComparison.OrdinalIgnoreCase) == true)
            return "load options that turn off driver integrity checks";
        return null;
    }

    private static string? RegistryViolation(RegistryTarget registry)
    {
        var location = registry.Location;
        if (location.Root != RegistryRoot.LocalMachine)
            return null;
        var key = location.KeyPath.TrimEnd('\\');

        var service = ServiceKey().Match(key);
        if (service.Success && ProtectedServices.Contains(service.Groups["name"].Value))
            return $"{service.Groups["name"].Value} is a protected service (anti-cheat, Windows Update or boot-critical)";

        if (ControlKey().Match(key) is { Success: true } control &&
            control.Groups["rest"].Value.Equals(@"Session Manager\Memory Management", StringComparison.OrdinalIgnoreCase) &&
            location.ValueName.StartsWith("VerifyDriver", StringComparison.OrdinalIgnoreCase))
            return "Driver Verifier is never turned on";

        if (key.Equals(WindowsUpdatePolicies, StringComparison.OrdinalIgnoreCase) &&
            location.ValueName.Equals("DeferQualityUpdatesPeriodInDays", StringComparison.OrdinalIgnoreCase) &&
            registry.Desired is { Kind: RegistryValueKind.DWord, Data.Length: 4 } days && BitConverter.ToUInt32(days.Data) > 30)
            return "quality updates are deferred at most 30 days, never blocked";

        if (BlocksUpdates(key, location.ValueName, registry.Desired))
            return "Windows Update is never blocked or cut off";

        return null;
    }

    private const string WindowsUpdatePolicies = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

    /// <summary>Policies (key\value) that stop updates arriving at all when set to anything but 0.</summary>
    private static readonly HashSet<string> UpdateBlockingPolicies = new(StringComparer.OrdinalIgnoreCase)
    {
        WindowsUpdatePolicies + @"\AU\NoAutoUpdate",
        WindowsUpdatePolicies + @"\AU\UseWUServer",
        WindowsUpdatePolicies + @"\DisableWindowsUpdateAccess",
        WindowsUpdatePolicies + @"\DoNotConnectToWindowsUpdateInternetLocations",
    };

    private static bool BlocksUpdates(string key, string valueName, RegistryValue desired)
    {
        var dword = desired is { Kind: RegistryValueKind.DWord, Data.Length: 4 } ? BitConverter.ToUInt32(desired.Data) : (uint?)null;
        if (UpdateBlockingPolicies.Contains($@"{key}\{valueName}"))
            return dword != 0;
        // AUOptions 1 means "never check for updates".
        return key.Equals(WindowsUpdatePolicies + @"\AU", StringComparison.OrdinalIgnoreCase) &&
               valueName.Equals("AUOptions", StringComparison.OrdinalIgnoreCase) &&
               dword is not (2 or 3 or 4 or 5 or 7);
    }

    /// <summary>
    /// The Users folder, a profile itself, or anything in a profile outside AppData (Documents, Downloads, Desktop,
    /// Pictures, OneDrive, Saved Games and any folder the user made there). Only AppData holds app caches.
    /// </summary>
    private static bool IsUserFolder(string path)
    {
        var normal = path.Replace('/', '\\').TrimEnd('\\');
        if (UsersFolder().IsMatch(normal))
            return true;
        var match = UserProfile().Match(normal);
        if (!match.Success)
            return false;
        if (!match.Groups["first"].Success)
            return true;
        var first = match.Groups["first"].Value;
        return UserFolders.Any(f => f.Equals(first, StringComparison.OrdinalIgnoreCase)) ||
               first.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase) ||
               !first.Equals("AppData", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEdge(string package) =>
        package.Contains("MicrosoftEdge", StringComparison.OrdinalIgnoreCase) ||
        package.Contains("EdgeWebView", StringComparison.OrdinalIgnoreCase) ||
        package.Equals("Microsoft.Edge", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^SYSTEM\\(CurrentControlSet|ControlSet\d{3})\\Services\\(?<name>[^\\]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ServiceKey();

    [GeneratedRegex(@"^SYSTEM\\(CurrentControlSet|ControlSet\d{3})\\Control\\(?<rest>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ControlKey();

    [GeneratedRegex(@"^[A-Za-z]:\\Users\\[^\\]+(\\(?<first>[^\\]+))?", RegexOptions.IgnoreCase)]
    private static partial Regex UserProfile();

    [GeneratedRegex(@"^[A-Za-z]:\\Users$", RegexOptions.IgnoreCase)]
    private static partial Regex UsersFolder();
}
