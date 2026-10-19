using Elynt.Core.Boot;
using Elynt.Core.Devices;
using Elynt.Core.Power;
using Elynt.Core.Registry;

namespace Elynt.Core.Tweaks;

/// <summary>
/// The Gaming section's possible-placebo group: popular tweaks with no reputable evidence of a gaming benefit. They
/// ship for people who want to try them, folded away at the bottom of Gaming, each saying why it is likely placebo.
/// Source: research/gaming-tweak-catalogue.md, rows 10–18, 20 and 21.
/// </summary>
public static class PlaceboTweaks
{
    private const string SystemProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    private static readonly Source BcdEditSet = new(
        "Microsoft: BCDEdit /set", "https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/bcdedit--set");

    private static readonly Source Mmcss = new(
        "Microsoft: Multimedia Class Scheduler Service",
        "https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service");

    public static TweakDefinition CoreParkingOff { get; } = new(
        Id: "gaming.placebo.core-parking-off",
        Section: Sections.Gaming,
        Name: "Turn off core parking",
        WhatItDoes:
            "Sets the active power plan's \"Processor performance core parking min cores\" (CPMINCORES) to 100%, " +
            "plugged in and on battery, so Windows never parks (idles) CPU cores.",
        Evidence:
            "Likely placebo, and harmful on some CPUs. Microsoft documents that 100% turns the core parking algorithm " +
            "off, but no reputable Windows 11 benchmark shows a gaming gain. On dual-CCD Ryzen X3D CPUs, parking is " +
            "how Windows keeps games on the 3D V-Cache cores, so turning it off can make games slower. It also costs " +
            "idle power.",
        Sources:
        [
            new("Microsoft: Core parking minimum cores (CPMinCores)",
                "https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-core-parking-cpmincores"),
            new("Hardware Busters: Ryzen 9 7950X3D core parking problem and solution",
                "https://hwbusters.com/cpu/amd-ryzen-9-7950x3d-core-parking-problem-solution/3/"),
        ],
        Tier: EvidenceTier.PossiblePlacebo,
        Undo: "Puts back exactly the values that plan had before, plugged in and on battery.",
        Steps: [new PowerSettingStep(PowerSettings.ProcessorSubgroup, PowerSettings.CoreParkingMinCores, "core parking min cores (CPMINCORES)", 100)])
    {
        HardwareNotes = [GamingTweaks.X3dCoreParking, GamingTweaks.LaptopPower],
    };

    private const string TcpipInterfaces = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\*";

    public static TweakDefinition NagleOff { get; } = new(
        Id: "gaming.placebo.nagle-off",
        Section: Sections.Gaming,
        Name: "Turn off Nagle's algorithm and delayed ACKs (TcpAckFrequency, TCPNoDelay)",
        WhatItDoes:
            "Sets TcpAckFrequency to 1 and TCPNoDelay to 1 on every network interface, so Windows acknowledges each TCP " +
            "packet straight away instead of waiting to batch them. Takes effect after a restart.",
        Evidence:
            "Likely placebo for games. Nagle's algorithm and delayed ACKs only affect TCP, while most real-time game " +
            "traffic is UDP; programs that care already turn Nagle off for their own connections with the TCP_NODELAY " +
            "socket option. Microsoft documents TcpAckFrequency (KB 328890) for request/response apps hurt by delayed " +
            "ACKs. Whether Windows still reads a per-interface TCPNoDelay value is not documented: the only Microsoft " +
            "article naming it is a Windows 2000-era Message Queuing fix, and no current Microsoft page lists it, so " +
            "that half of the tweak may do nothing at all. No benchmark shows a gaming effect.",
        Sources:
        [
            new("Microsoft KB 328890: Registry entry for controlling the TCP acknowledgment behavior",
                "https://support.microsoft.com/kb/328890"),
            new("Microsoft: TCP delayed ACK combined with Nagle algorithm can badly impact communication performance",
                "https://learn.microsoft.com/en-us/archive/blogs/nettracer/tcp-delayed-ack-combined-with-nagle-algorithm-can-badly-impact-communication-performance"),
            new("Microsoft KB 235624: TCP/IP Nagle algorithm for Message Queuing (the only Microsoft mention of TCPNoDelay)",
                "https://support.microsoft.com/en-us/help/235624/fix-tcp-ip-nagle-algorithm-for-microsoft-message-queue-server-can-be-d"),
            new("Wikipedia: Nagle's algorithm", "https://en.wikipedia.org/wiki/Nagle%27s_algorithm"),
        ],
        Tier: EvidenceTier.PossiblePlacebo,
        Undo: "Puts back exactly what each interface had before (normally neither value exists), then needs a restart.",
        Steps:
        [
            new RegistryEachKeyStep(RegistryRoot.LocalMachine, TcpipInterfaces, null, null, "TcpAckFrequency", RegistryValue.DWord(1)),
            new RegistryEachKeyStep(RegistryRoot.LocalMachine, TcpipInterfaces, null, null, "TCPNoDelay", RegistryValue.DWord(1)),
        ])
    {
        Risks = RiskFlags.NeedsReboot,
    };

    /// <summary>The Display device class (graphics cards).</summary>
    private const string DisplayClass = "{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static TweakDefinition MsiModeGpu { get; } = new(
        Id: "gaming.placebo.msi-mode-gpu",
        Section: Sections.Gaming,
        Name: "Force MSI mode on the graphics card",
        WhatItDoes:
            "Sets MSISupported to 1 for every PCI graphics card, telling Windows to use message-signaled interrupts " +
            "for it instead of line-based ones. Takes effect after a restart.",
        Evidence:
            "Likely placebo, and risky. Microsoft documents MSISupported for driver authors, not as a user tweak: a " +
            "driver that supports MSI already turns it on in its own setup file, and current NVIDIA, AMD and Intel " +
            "drivers do. No reputable benchmark shows a gaming gain; the evidence is forum threads. Forcing it on a " +
            "device or driver that doesn't support it can crash Windows or stop it booting, and fixing that needs " +
            "Safe Mode.",
        Sources:
        [
            new("Microsoft: Enabling message-signaled interrupts in the registry",
                "https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/enabling-message-signaled-interrupts-in-the-registry"),
            new("Guru3D forum: MSI utility thread", "https://forums.guru3d.com/threads/better-msi-utility.458390/post-6375142"),
        ],
        Tier: EvidenceTier.PossiblePlacebo,
        Undo:
            "Puts back exactly what each graphics card had before, then needs a restart. If Windows won't start after " +
            "this, start in Safe Mode and undo it there.",
        Steps:
        [
            new RegistryEachKeyStep(
                RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Enum\PCI\*\*", new RegistryKeyFilter("ClassGUID", DisplayClass),
                @"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties", "MSISupported", RegistryValue.DWord(1)),
        ])
    {
        Risks = RiskFlags.NeedsReboot | RiskFlags.CanStopBoot,
    };

    public static TweakDefinition NicInterruptModerationOff { get; } = new(
        Id: "gaming.placebo.nic-interrupt-moderation-off",
        Section: Sections.Gaming,
        Name: "Turn off interrupt moderation on network adapters",
        WhatItDoes:
            "Sets the standard *InterruptModeration setting to Disabled on every network adapter that has it (Device " +
            "Manager > adapter > Advanced > Interrupt Moderation), so the adapter interrupts the CPU for every packet " +
            "instead of batching them. Takes effect after a restart.",
        Evidence:
            "Likely placebo. Intel says lowering interrupt moderation can help small-packet workloads but costs CPU " +
            "time, that its default is optimal for common configurations, and not to change it without verifying it " +
            "helps. The saving is microseconds against internet ping measured in milliseconds. No gaming benchmark " +
            "shows an effect.",
        Sources:
        [
            new("Intel: Interrupt Moderation Rate",
                "https://edc.intel.com/content/www/us/en/design/products/ethernet/adapters-and-devices-user-guide/interrupt-moderation-rate"),
            new("Microsoft: Standardized INF keywords for interrupt moderation",
                "https://learn.microsoft.com/en-us/windows-hardware/drivers/network/standardized-inf-keywords-for-interrupt-moderation"),
        ],
        Tier: EvidenceTier.PossiblePlacebo,
        Undo: "Puts back exactly what each adapter had before, then needs a restart.",
        Steps:
        [
            new RegistryEachKeyStep(
                RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}\*",
                new RegistryKeyFilter("*InterruptModeration"), null, "*InterruptModeration", RegistryValue.String("0")),
        ])
    {
        Risks = RiskFlags.NeedsReboot,
    };

    public static TweakDefinition NetworkThrottlingOff { get; } = new(
        Id: "gaming.placebo.network-throttling-off",
        Section: Sections.Gaming,
        Name: "Turn off network throttling (NetworkThrottlingIndex)",
        WhatItDoes:
            "Sets NetworkThrottlingIndex to FFFFFFFF, which stops Windows limiting how many network packets it " +
            "processes per millisecond while audio or video is playing. Takes effect after a restart.",
        Evidence:
            "Likely placebo for games. Mark Russinovich explains the limit is about 10,000 packets per second and was " +
            "added to keep audio from glitching during heavy transfers; it slowed gigabit file copies, not games. Game " +
            "traffic is a few hundred packets per second at most, far below the limit. No benchmark shows a gaming " +
            "effect. Turning it off can bring back the audio glitches it was made to prevent.",
        Sources:
        [
            new("Mark Russinovich: Vista multimedia playback and network throughput",
                "https://learn.microsoft.com/en-us/archive/blogs/markrussinovich/vista-multimedia-playback-and-network-throughput"),
            Mmcss,
        ],
        Tier: EvidenceTier.PossiblePlacebo,
        Undo: "Puts back exactly the value that was there before (Windows' default is 10), then needs a restart.",
        Steps: [new RegistryValueStep(new(RegistryRoot.LocalMachine, SystemProfile, "NetworkThrottlingIndex"), RegistryValue.DWord(0xFFFFFFFF))])
    {
        Risks = RiskFlags.NeedsReboot,
    };

    public static TweakDefinition MmcssGames { get; } = new(
        Id: "gaming.placebo.mmcss-games",
        Section: Sections.Gaming,
        Name: "Raise the multimedia scheduler's priority for games (SystemResponsiveness, MMCSS)",
        WhatItDoes:
            "Sets SystemResponsiveness to 10, so the multimedia scheduler (MMCSS) keeps only 10% of the CPU for " +
            "background work instead of 20%, and gives its \"Games\" task high priority (Priority 6, GPU Priority 8, " +
            "Scheduling Category High, SFIO Priority High). Takes effect after a restart.",
        Evidence:
            "Likely placebo. Microsoft documents that values below 10 and above 100 are clamped to 20, so the popular " +
            "\"SystemResponsiveness = 0\" is the same as the default 20; this tweak uses 10, the lowest value that " +
            "changes anything. The Games task only affects threads a game registers with MMCSS " +
            "(AvSetMmThreadCharacteristics), which many games never do. No benchmark shows a gaming effect.",
        Sources:
        [
            Mmcss,
            new("Microsoft: AvSetMmThreadCharacteristicsW",
                "https://learn.microsoft.com/en-us/windows/win32/api/avrt/nf-avrt-avsetmmthreadcharacteristicsw"),
        ],
        Tier: EvidenceTier.PossiblePlacebo,
        Undo: "Puts back exactly the values that were there before (Windows' default SystemResponsiveness is 20), then needs a restart.",
        Steps:
        [
            new RegistryValueStep(new(RegistryRoot.LocalMachine, SystemProfile, "SystemResponsiveness"), RegistryValue.DWord(10)),
            new RegistryValueStep(new(RegistryRoot.LocalMachine, SystemProfile + @"\Tasks\Games", "Priority"), RegistryValue.DWord(6)),
            new RegistryValueStep(new(RegistryRoot.LocalMachine, SystemProfile + @"\Tasks\Games", "GPU Priority"), RegistryValue.DWord(8)),
            new RegistryValueStep(new(RegistryRoot.LocalMachine, SystemProfile + @"\Tasks\Games", "Scheduling Category"), RegistryValue.String("High")),
            new RegistryValueStep(new(RegistryRoot.LocalMachine, SystemProfile + @"\Tasks\Games", "SFIO Priority"), RegistryValue.String("High")),
        ])
    {
        Risks = RiskFlags.NeedsReboot,
    };

    public static TweakDefinition Win32PrioritySeparation { get; } = new(
        Id: "gaming.placebo.win32-priority-separation",
        Section: Sections.Gaming,
        Name: "Favour the foreground program more (Win32PrioritySeparation)",
        WhatItDoes:
            "Sets Win32PrioritySeparation to 0x26 (38): short, variable CPU time slices with the largest boost for the " +
            "window in front. Windows' default for desktops is 2, which already favours the foreground program.",
        Evidence:
            "Likely placebo. Microsoft documents what the bits do (time-slice length, fixed or variable, and the " +
            "foreground boost), but no reputable benchmark shows a gaming benefit; the claims are forum posts. Games " +
            "spend their time waiting on the GPU or their own threads, which a different time slice doesn't change.",
        Sources:
        [
            new("Microsoft TechNet: Win32PrioritySeparation", "https://technet.microsoft.com/en-us/library/cc940378.aspx"),
            new("Blur Busters forum: Win32PrioritySeparation discussion", "https://forums.blurbusters.com/viewtopic.php?p=69543"),
        ],
        Tier: EvidenceTier.PossiblePlacebo,
        Undo: "Puts back exactly the value that was there before (normally 2).",
        Steps:
        [
            new RegistryValueStep(
                new(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation"),
                RegistryValue.DWord(0x26)),
        ]);

    public static TweakDefinition DynamicTickOff { get; } = new(
        Id: "gaming.placebo.dynamic-tick-off",
        Section: Sections.Gaming,
        Name: "Turn off dynamic tick (disabledynamictick)",
        WhatItDoes:
            "Sets the boot setting disabledynamictick to yes, so Windows keeps its timer ticking even when the CPU is " +
            "idle instead of skipping ticks to save power. Takes effect after a restart.",
        Evidence:
            "Likely placebo. BCDEdit's documentation says this option should only be used for debugging. Only forum " +
            "anecdotes claim lower latency; no benchmark shows it. It costs idle power, which matters most on laptops.",
        Sources:
        [
            BcdEditSet,
            new("Blur Busters forum: disabledynamictick discussion", "https://forums.blurbusters.com/viewtopic.php?p=107460"),
        ],
        Tier: EvidenceTier.PossiblePlacebo,
        Undo: "Sets disabledynamictick back to exactly what it was (normally not set), then needs a restart.",
        Steps: [new BootSettingStep("disabledynamictick", "Yes")])
    {
        Risks = RiskFlags.NeedsReboot,
        HardwareNotes = [GamingTweaks.LaptopPower],
    };

    public static TweakDefinition HpetOff { get; } = new(
        Id: "gaming.placebo.hpet-off",
        Section: Sections.Gaming,
        Name: "Disable the High precision event timer (HPET) device",
        WhatItDoes:
            "Disables \"High precision event timer\" under System devices in Device Manager. Takes effect after a " +
            "restart.",
        Evidence:
            "Likely placebo. Windows already prefers the CPU's fast TSC timer over HPET for QueryPerformanceCounter " +
            "whenever the TSC is reliable, so on modern PCs HPET is usually not what games read. The claims of " +
            "smoother play come from forum threads without controlled measurements. Some platforms use HPET for other " +
            "timers. (Forcing HPET on with useplatformclock is the opposite tweak and is harmful; the main Gaming " +
            "list removes that.)",
        Sources:
        [
            new("Microsoft: Acquiring high-resolution time stamps",
                "https://learn.microsoft.com/en-us/windows/win32/sysinfo/acquiring-high-resolution-time-stamps"),
            new("Blur Busters forum: HPET discussion", "https://forums.blurbusters.com/viewtopic.php?p=93173"),
        ],
        Tier: EvidenceTier.PossiblePlacebo,
        Undo: "Enables the device again if it was enabled before, then needs a restart.",
        Steps: [new DeviceEnabledStep(@"ACPI\PNP0103", "High precision event timer", Enabled: false)])
    {
        Risks = RiskFlags.NeedsReboot,
    };

    public static TweakDefinition StandbyListCleaner { get; } = new(
        Id: "gaming.placebo.standby-list-cleaner",
        Section: Sections.Gaming,
        Name: "Empty the standby memory list automatically",
        WhatItDoes:
            "The app's service checks every 10 seconds and empties Windows' standby list (memory holding recently " +
            "used files) when the list is over 1 GB and less than 1 GB of memory is free, like ISLC or RAMMap's " +
            "\"Empty Standby List\". Nothing else on the PC changes; switching it off stops the checks.",
        Evidence:
            "Likely placebo. The standby list is a cache Windows hands out as soon as a program needs memory, so it " +
            "doesn't take memory from games. The Process Lasso developer, who added the feature on request, says most " +
            "people should not use it: Windows already trims the list, and emptying it costs disk reads later. The " +
            "one concrete report is a stutter on Windows 10 1809 that went away in later versions.",
        Sources:
        [
            new("Bitsum community: standby list purging (Process Lasso developer)", "https://community.bitsum.com/forum/index.php?msg=30475"),
            new("Microsoft Learn: RAMMap", "https://learn.microsoft.com/en-us/sysinternals/downloads/rammap"),
        ],
        Tier: EvidenceTier.PossiblePlacebo,
        Undo: "Stops the checks by putting the app's own switch back as it was.",
        Steps: [new RegistryValueStep(Memory.StandbyListCleaner.Switch, RegistryValue.DWord(1))]);

    public static TweakDefinition VisualEffectsPerformance { get; } = new(
        Id: "gaming.placebo.visual-effects-performance",
        Section: Sections.Gaming,
        Name: "Adjust visual effects for best performance",
        WhatItDoes:
            "Does what System Properties > Performance Options > \"Adjust for best performance\" does: turns off window " +
            "animations, shadows, fading and smooth effects, and Aero Peek. The desktop looks plainer. Takes full " +
            "effect after you sign out and back in, or restart.",
        Evidence:
            "Likely placebo for games. These are desktop effects drawn by the Desktop Window Manager, which steps " +
            "aside for fullscreen and independent-flip games, so they cost nothing while you play. No benchmark shows " +
            "an in-game gain. It can make a very slow PC's desktop feel snappier.",
        Sources:
        [
            new("Microsoft DirectX blog: Demystifying fullscreen optimizations",
                "https://devblogs.microsoft.com/directx/demystifying-full-screen-optimizations"),
            new("WinUtil: Set display for performance", "https://winutil.christitus.com/code-reference/tweaks/z--advanced-tweaks---caution/display/"),
        ],
        Tier: EvidenceTier.PossiblePlacebo,
        Undo: "Puts back exactly the values that were there before, then needs you to sign out or restart.",
        Steps:
        [
            new RegistryValueStep(
                new(RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting"),
                RegistryValue.DWord(2)),
            new RegistryValueStep(
                new(RegistryRoot.CurrentUser, @"Control Panel\Desktop", "UserPreferencesMask"),
                new RegistryValue(RegistryValueKind.Binary, [0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00])),
            new RegistryValueStep(new(RegistryRoot.CurrentUser, @"Control Panel\Desktop", "DragFullWindows"), RegistryValue.String("0")),
            new RegistryValueStep(new(RegistryRoot.CurrentUser, @"Control Panel\Desktop\WindowMetrics", "MinAnimate"), RegistryValue.String("0")),
            new RegistryValueStep(
                new(RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations"),
                RegistryValue.DWord(0)),
            new RegistryValueStep(
                new(RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewAlphaSelect"),
                RegistryValue.DWord(0)),
            new RegistryValueStep(
                new(RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewShadow"),
                RegistryValue.DWord(0)),
            new RegistryValueStep(new(RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\DWM", "EnableAeroPeek"), RegistryValue.DWord(0)),
        ])
    {
        Risks = RiskFlags.NeedsReboot,
    };

    public static IReadOnlyList<TweakDefinition> All { get; } =
    [
        CoreParkingOff, MsiModeGpu, NagleOff, NetworkThrottlingOff, NicInterruptModerationOff, MmcssGames, Win32PrioritySeparation, DynamicTickOff, HpetOff, StandbyListCleaner, VisualEffectsPerformance,
    ];
}
