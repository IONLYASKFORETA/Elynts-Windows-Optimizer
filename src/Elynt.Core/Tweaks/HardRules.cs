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
        RegistryPatternTarget pattern => RegistryViolation(new RegistryTarget(pattern.Pattern, pattern.Desired)),
        ServiceTarget service when ProtectedServices.Contains(service.ServiceName) =>
            $"{service.ServiceName} is a protected service (anti-cheat, Windows Update or boot-critical)",
        DriverTarget driver when VulnerableDrivers.Contains(Path.GetFileName(driver.DriverFile.Replace('\\', '/'))) =>
            $"{driver.DriverFile} is a known-vulnerable driver that anti-cheats and Windows block",
        FileTarget file when IsUserFolder(file.Path) => "user folders and game saves are never touched",
        PackageTarget package when IsEdge(package.PackageName) => "Edge and WebView2 are never removed",
        ScanExclusionTarget exclusion => ExclusionViolation(exclusion.Folder),
        UpdatePauseTarget pause when pause.Days is < 1 or > UpdateControl.UpdatePauseStep.MaxDays =>
            $"updates are paused at most {UpdateControl.UpdatePauseStep.MaxDays} days (Windows' own limit), never blocked",
        _ => null,
    };

    /// <summary>
    /// Defender exclusions are only for one game's own folder. A whole drive, Windows, the shared program folders,
    /// a user profile, user folders or temp folders would leave the places malware lands unscanned.
    /// </summary>
    private static string? ExclusionViolation(string folder)
    {
        var path = folder.Replace('/', '\\').TrimEnd('\\');
        var parts = path.Split('\\');
        if (!DriveLetter().IsMatch(parts[0]) || parts.Any(p => p is "" or "." or ".."))
            return "Defender exclusions must be a full folder path";
        if (parts.Length == 1)
            return "a whole drive is never excluded from Defender scans";
        if (parts[1].Equals("Windows", StringComparison.OrdinalIgnoreCase))
            return "Windows' own folders are never excluded from Defender scans";
        if (parts.Length == 2 && SharedRoots.Contains(parts[1]))
            return $"{path} is shared by every program, so it is never excluded from Defender scans";
        if (parts[1].Equals("Users", StringComparison.OrdinalIgnoreCase) &&
            (parts.Length <= 3 || IsUserFolder(path) || path.Contains(@"\AppData\Local\Temp", StringComparison.OrdinalIgnoreCase)))
            return "user profiles, user folders and temp folders are never excluded from Defender scans";
        return null;
    }

    private static readonly HashSet<string> SharedRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "Program Files", "Program Files (x86)", "ProgramData", "Users",
    };

    [GeneratedRegex(@"^[A-Za-z]:$")]
    private static partial Regex DriveLetter();

    /// <summary>
    /// The rules above, plus the ones that need to look at the PC: a driver whose current start type is Boot is
    /// boot-critical, whatever its name.
    /// </summary>
    public static string? Violation(ChangeTarget target, Services.IServiceManager? services)
    {
        if (Violation(target) is { } violation)
            return violation;

        var name = target switch
        {
            ServiceTarget service => service.ServiceName,
            RegistryTarget { Location.Root: RegistryRoot.LocalMachine } registry when
                ServiceKey().Match(registry.Location.KeyPath) is { Success: true } key => key.Groups["name"].Value,
            _ => null,
        };
        if (name is null || services is null)
            return null;

        Services.ServiceInfo? current;
        try
        {
            current = services.Find(name);
        }
        catch (Exception ex)
        {
            // Not knowing is treated as protected: a boot driver set wrong stops Windows from starting.
            return $"couldn't check whether {name} is a boot driver ({ex.Message})";
        }
        return current?.Config.Start == ServiceStart.Boot
            ? $"{name} is a boot driver: Windows can't start without it"
            : null;
    }

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

        if (PauseViolation(key, location.ValueName, registry.Desired) is { } pause)
            return pause;

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

    /// <summary>
    /// The Settings app's pause values. A fixed end date can't be bounded from today, so quality-update pause dates
    /// are written only through <see cref="UpdatePauseTarget"/>, at most 35 days ahead; raising Windows' own pause
    /// limit is refused outright.
    /// </summary>
    private static string? PauseViolation(string key, string valueName, RegistryValue desired)
    {
        if (!key.Equals(UpdateControl.UpdatePauseStep.UxSettings, StringComparison.OrdinalIgnoreCase))
            return null;
        if (valueName.Equals("FlightSettingsMaxPauseDays", StringComparison.OrdinalIgnoreCase) &&
            !(desired is { Kind: RegistryValueKind.DWord, Data.Length: 4 } days && BitConverter.ToUInt32(desired.Data) <= UpdateControl.UpdatePauseStep.MaxDays))
            return $"Windows' pause limit is never raised past {UpdateControl.UpdatePauseStep.MaxDays} days";
        return QualityPauseValues.Contains(valueName)
            ? $"quality updates are paused only through the app's pause control, at most {UpdateControl.UpdatePauseStep.MaxDays} days, never blocked"
            : null;
    }

    private static readonly HashSet<string> QualityPauseValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime", "PauseUpdatesStartTime", "PauseUpdatesExpiryTime",
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
        // winget ids: Microsoft.Edge, Microsoft.Edge.Beta/.Dev, Microsoft.EdgeWebView2Runtime; ARP ids name it with a space.
        package.StartsWith("Microsoft.Edge", StringComparison.OrdinalIgnoreCase) ||
        package.Contains("Microsoft Edge", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^SYSTEM\\(CurrentControlSet|ControlSet\d{3})\\Services\\(?<name>[^\\]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ServiceKey();

    [GeneratedRegex(@"^SYSTEM\\(CurrentControlSet|ControlSet\d{3})\\Control\\(?<rest>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ControlKey();

    [GeneratedRegex(@"^[A-Za-z]:\\Users\\[^\\]+(\\(?<first>[^\\]+))?", RegexOptions.IgnoreCase)]
    private static partial Regex UserProfile();

    [GeneratedRegex(@"^[A-Za-z]:\\Users$", RegexOptions.IgnoreCase)]
    private static partial Regex UsersFolder();
}
