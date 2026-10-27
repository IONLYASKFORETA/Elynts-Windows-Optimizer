namespace Elynt.Core.Repair;

public static class RepairToolIds
{
    public const string SystemFiles = "repair.system-files";
    public const string DiskCheck = "repair.disk-check";
    public const string DiskFixAtRestart = "repair.disk-fix-at-restart";
    public const string NetworkRepair = "repair.network";
    public const string FullNetworkReset = "repair.network-full-reset";
    public const string WindowsUpdateReset = "repair.windows-update";
    public const string IconAndFontCache = "repair.icon-font-cache";
    public const string StoreCache = "repair.store-cache";
    public const string WindowsSecurityApp = "repair.windows-security-app";
    public const string Runtimes = "repair.runtimes";
}

/// <summary>The Repair section's tools, in the order the UI lists them.</summary>
public sealed class RepairCatalogue(IReadOnlyList<RepairToolDefinition> tools)
{
    public IReadOnlyList<RepairToolDefinition> Tools { get; } = tools;

    public bool TryGet(string id, out RepairToolDefinition tool)
    {
        tool = Tools.FirstOrDefault(t => t.Id == id)!;
        return tool is not null;
    }

    private static readonly (string Name, string WingetId)[] Runtimes =
    [
        ("Visual C++ 2015+ (64-bit)", "Microsoft.VCRedist.2015+.x64"),
        ("Visual C++ 2015+ (32-bit)", "Microsoft.VCRedist.2015+.x86"),
        ("Visual C++ 2013 (64-bit)", "Microsoft.VCRedist.2013.x64"),
        ("Visual C++ 2013 (32-bit)", "Microsoft.VCRedist.2013.x86"),
        ("Visual C++ 2012 (64-bit)", "Microsoft.VCRedist.2012.x64"),
        ("Visual C++ 2012 (32-bit)", "Microsoft.VCRedist.2012.x86"),
        ("Visual C++ 2010 (64-bit)", "Microsoft.VCRedist.2010.x64"),
        ("Visual C++ 2010 (32-bit)", "Microsoft.VCRedist.2010.x86"),
        ("Visual C++ 2008 (64-bit)", "Microsoft.VCRedist.2008.x64"),
        ("Visual C++ 2008 (32-bit)", "Microsoft.VCRedist.2008.x86"),
        ("DirectX legacy runtime", "Microsoft.DirectX"),
        (".NET 8 Desktop Runtime", "Microsoft.DotNet.DesktopRuntime.8"),
        (".NET 9 Desktop Runtime", "Microsoft.DotNet.DesktopRuntime.9"),
        (".NET 10 Desktop Runtime", "Microsoft.DotNet.DesktopRuntime.10"),
    ];

    public static RepairCatalogue Default { get; } = new(
    [
        new(RepairToolIds.SystemFiles,
            "Repair Windows system files",
            "Runs DISM to repair Windows' component store from Windows Update, then SFC to check every protected system file and replace damaged ones from that store. Takes 10–30 minutes.",
            RestartNeed.None,
            "No restart needed. If SFC repairs files that are in use, it says so and they finish at the next restart.",
            [
                new RunProgramStep("DISM component store repair",
                    new ProgramRun("dism.exe", ["/Online", "/Cleanup-Image", "/RestoreHealth"]),
                    ToolOutput.Dism),
                // SFC still runs when DISM fails: it can often repair from what the store already holds.
                new RunProgramStep("SFC system file check",
                    new ProgramRun("sfc.exe", ["/scannow"], ConsoleText.Unicode),
                    ToolOutput.Sfc,
                    AlwaysRun: true),
            ]),

        new(RepairToolIds.DiskCheck,
            "Check the Windows drive for errors",
            "Scans the Windows drive's file system while Windows runs. Read-only: it changes nothing. If it finds problems, you're offered a full fix at the next restart.",
            RestartNeed.None,
            "No restart needed for the check. A fix, if you choose one, runs at the next restart.",
            [
                new RunProgramStep("Read-only disk scan",
                    new ProgramRun("chkdsk.exe", [RunProgramStep.SystemDriveToken, "/scan"]),
                    ToolOutput.DiskScan),
            ]),

        new(RepairToolIds.DiskFixAtRestart,
            "Fix disk errors at the next restart",
            "Marks the Windows drive so Windows checks and fixes it before it starts next time. That check can take a while; don't turn the PC off during it.",
            RestartNeed.Restart,
            "Needs a restart: the fix runs while Windows starts.",
            [
                new RunProgramStep("Schedule the disk fix",
                    new ProgramRun("fsutil.exe", ["dirty", "set", RunProgramStep.SystemDriveToken]),
                    ToolOutput.ScheduleDiskFix),
            ],
            IsFollowUp: true),

        new(RepairToolIds.NetworkRepair,
            "Repair the network connection",
            "Clears the DNS cache, resets Winsock and resets the TCP/IP settings to Windows' defaults. Keeps your network adapters, VPN adapters and Wi-Fi networks.",
            RestartNeed.Restart,
            "Needs a restart to finish the Winsock and TCP/IP reset.",
            [
                new RunProgramStep("Clear the DNS cache",
                    new ProgramRun("ipconfig.exe", ["/flushdns"]), ToolOutput.Simple("DNS cache cleared."), AlwaysRun: true),
                new RunProgramStep("Reset Winsock",
                    new ProgramRun("netsh.exe", ["winsock", "reset"]), ToolOutput.Simple("Winsock reset.", restartNeeded: true), AlwaysRun: true),
                new RunProgramStep("Reset TCP/IP",
                    new ProgramRun("netsh.exe", ["int", "ip", "reset"]), ToolOutput.Simple("TCP/IP settings reset.", restartNeeded: true), AlwaysRun: true),
            ]),

        new(RepairToolIds.FullNetworkReset,
            "Full network reset",
            "Removes every network adapter and sets networking back to Windows' defaults, like Settings › Network reset. Windows reinstalls the adapters after the restart.",
            RestartNeed.Restart,
            "Needs a restart: adapters come back only after it.",
            [
                new RunProgramStep("Remove and reinstall network adapters",
                    new ProgramRun("netcfg.exe", ["-d"]), ToolOutput.Simple("Network adapters will be reinstalled at the restart.", restartNeeded: true)),
            ],
            Confirmation: "This removes VPN adapters, virtual switches and their settings, and you'll have to reconnect to Wi-Fi networks. VPN software may need reinstalling. It can't be undone."),

        new(RepairToolIds.WindowsUpdateReset,
            "Repair Windows Update",
            "Stops the update services, clears the update download cache (SoftwareDistribution) and the update signature cache (catroot2), then starts the services again. Fixes updates stuck downloading or failing. Your update history list is cleared; installed updates are not affected.",
            RestartNeed.None,
            "No restart needed.",
            [
                StopService("UsoSvc"),
                StopService("wuauserv"),
                StopService("bits"),
                StopService("cryptsvc"),
                new ClearFolderStep("Clear the update download cache", RepairFolder.Windows, "SoftwareDistribution", "*", IncludeSubfolders: true),
                new ClearFolderStep("Clear the update signature cache", RepairFolder.Windows, @"System32\catroot2", "*", IncludeSubfolders: true),
                StartService("cryptsvc"),
                StartService("bits"),
                StartService("wuauserv"),
                StartService("UsoSvc"),
            ]),

        new(RepairToolIds.IconAndFontCache,
            "Rebuild icon and font caches",
            "Deletes Windows' icon cache and font cache so they are rebuilt. Fixes blank or wrong icons and fonts that look broken.",
            RestartNeed.Restart,
            "Needs a restart: the caches are rebuilt while Windows starts and you sign in.",
            [
                new ClearFolderStep("Clear the icon cache", RepairFolder.UserLocalAppData, "", "IconCache.db", IncludeSubfolders: false),
                new ClearFolderStep("Clear the Explorer icon cache", RepairFolder.UserLocalAppData, @"Microsoft\Windows\Explorer", "iconcache_*.db", IncludeSubfolders: false),
                StopService("FontCache"),
                new ClearFolderStep("Clear the font cache", RepairFolder.Windows, @"ServiceProfiles\LocalService\AppData\Local\FontCache", "*", IncludeSubfolders: true),
                new ClearFolderStep("Clear the boot font cache", RepairFolder.Windows, "System32", "FNTCACHE.DAT", IncludeSubfolders: false),
                StartService("FontCache"),
            ]),

        new(RepairToolIds.StoreCache,
            "Reset the Microsoft Store cache",
            "Clears the Microsoft Store's local cache, like wsreset does. Fixes a Store that won't open or won't download. Your apps and sign-in are kept.",
            RestartNeed.None,
            "No restart needed. Close the Store first.",
            [
                new ClearFolderStep("Clear the Store cache", RepairFolder.UserLocalAppData, $@"Packages\{StorePackage}\LocalCache", "*", IncludeSubfolders: true),
            ],
            Requires: new(RepairFolder.UserLocalAppData, $@"Packages\{StorePackage}", "The Microsoft Store isn't installed for you, so there's no Store cache to reset.")),

        // No folder requirement: a broken install may not have one, and the step itself says when the app is missing.
        new(RepairToolIds.WindowsSecurityApp,
            WindowsSecurityAppName,
            "Resets the Windows Security app for your account and registers it again from the copy Windows keeps, like Settings › Apps › Installed apps › Windows Security › Advanced options › Reset. Fixes Windows Security opening as a blank white window. Microsoft Defender itself and its settings aren't changed.",
            RestartNeed.None,
            "No restart needed. Close Windows Security first. If it's still blank afterwards, restart the PC and open it again.",
            [
                new ResetAppStep("Reset and register Windows Security again", "Microsoft.SecHealthUI", ToolOutput.ResetApp("Windows Security")),
            ]),

        new(RepairToolIds.Runtimes,
            "Reinstall game runtimes",
            "Reinstalls Visual C++ (2008–2015+, 32- and 64-bit), DirectX (June 2010 legacy files) and the .NET Desktop runtimes through winget. Fixes games that crash at launch with a missing DLL such as MSVCP140.dll or d3dx9_43.dll.",
            RestartNeed.Maybe,
            "Sometimes needs a restart; the result says so if an installer asks for one.",
            [.. Runtimes.Select(r => new RunProgramStep(r.Name,
                new ProgramRun("winget.exe",
                [
                    "install", "--id", r.WingetId, "--exact", "--source", "winget", "--force", "--silent",
                    "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
                ]),
                ToolOutput.Winget(r.Name),
                AlwaysRun: true))]),
    ]);

    private const string StorePackage = "Microsoft.WindowsStore_8wekyb3d8bbwe";

    /// <summary>Named here so the Tamper Protection guide can point to the tool by the name the Repair page shows.</summary>
    public const string WindowsSecurityAppName = "Repair the Windows Security app";


    private static RunProgramStep StopService(string service) =>
        new($"Stop {service}", new ProgramRun("net.exe", ["stop", service, "/y"]), ToolOutput.ServiceControl($"{service} stopped."));

    // Starting runs even after a failure, so the tool never leaves a service it stopped switched off.
    private static RunProgramStep StartService(string service) =>
        new($"Start {service}", new ProgramRun("net.exe", ["start", service]), ToolOutput.ServiceControl($"{service} started."), AlwaysRun: true);
}
