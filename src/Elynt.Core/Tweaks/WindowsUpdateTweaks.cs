using Elynt.Core.Registry;

namespace Elynt.Core.Tweaks;

/// <summary>
/// The Windows Update section. Every control is a plain journaled registry tweak, so undo puts back exactly what
/// was there. Quality updates are only ever paused for at most 35 days or deferred at most 30 days, and the
/// update service itself is never touched (see <see cref="HardRules"/>).
/// </summary>
public static class WindowsUpdateTweaks
{
    /// <summary>The Settings app's own pause and active-hours values. Honoured on every edition, Home included.</summary>
    private const string UxSettings = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

    private const string Policies = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

    /// <summary>
    /// Microsoft's Policy CSP lists Windows Update for Business (target version, deferral, driver exclusion) and
    /// Delivery Optimization's download mode for these editions only; Home is not listed. Checked against the docs,
    /// not on a real Home PC.
    /// </summary>
    private static readonly Applicability BusinessEditions =
        new(Editions: [WindowsEdition.Pro, WindowsEdition.Education, WindowsEdition.Enterprise]);

    private static readonly Source Restarts = new(
        "Microsoft: Manage device restarts after updates", "https://learn.microsoft.com/en-us/windows/deployment/update/waas-restart");

    private static readonly Source UpdatePolicies = new(
        "Microsoft: Policy CSP - Update", "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-update");

    /// <summary>
    /// Vanguard and Ricochet want a current Windows: research/anti-cheat-requirements.md row 16 ("block or defer
    /// Windows Update").
    /// </summary>
    private static readonly IReadOnlyList<AntiCheatConflict> OutdatedWindowsConflicts =
    [
        new(AntiCheat.Vanguard, AntiCheatSeverity.WontLaunch, "VAN: RESTRICTION: a PC with outdated Windows may be restricted from playing"),
        new(AntiCheat.Ricochet, AntiCheatSeverity.Restricted, "Some TPM attestation failures are fixed by a Windows update"),
    ];

    public static TweakDefinition Pause { get; } = new(
        Id: "update.pause",
        Section: Sections.WindowsUpdate,
        Name: "Pause updates for 5 weeks",
        WhatItDoes:
            "Pauses feature and quality updates for 35 days from today, the longest Windows itself allows. Updates " +
            "resume on their own after that, and Windows installs the waiting ones before it lets you pause again. " +
            "Security fixes wait while paused, so use \"Check and install now\" if one matters; it still works while paused.",
        Evidence:
            "Uses the same pause values the Settings app writes when you pick \"Pause for 5 weeks\". Pausing is for " +
            "keeping a working setup stable during a tournament or a busy week, not for speed.",
        Sources: [UpdatePolicies],
        Tier: EvidenceTier.Situational,
        Undo: "Puts back the previous pause values. If updates weren't paused before, they resume straight away.",
        Steps:
        [
            PauseValue("PauseUpdatesStartTime", isEnd: false),
            PauseValue("PauseUpdatesExpiryTime", isEnd: true),
            PauseValue("PauseFeatureUpdatesStartTime", isEnd: false),
            PauseValue("PauseFeatureUpdatesEndTime", isEnd: true),
            PauseValue("PauseQualityUpdatesStartTime", isEnd: false),
            PauseValue("PauseQualityUpdatesEndTime", isEnd: true),
        ])
    {
        Risks = RiskFlags.SecurityRisk,
        AntiCheat = OutdatedWindowsConflicts,
        // Windows ends the pause itself; putting it back would turn a bounded pause into an endless one.
        Expires = true,
    };

    /// <summary>Windows 11 version names by build. Only versions still serviced for Home and Pro are offered.</summary>
    public static TweakDefinition Pin24H2 { get; } = Pin("24H2", 26100);

    public static TweakDefinition Pin25H2 { get; } = Pin("25H2", 26200);

    public static TweakDefinition DeferQuality7 { get; } = DeferQuality(7);

    public static TweakDefinition DeferQuality30 { get; } = DeferQuality(30);

    public static TweakDefinition ExcludeDrivers { get; } = new(
        Id: "update.exclude-drivers",
        Section: Sections.WindowsUpdate,
        Name: "Don't get drivers from Windows Update",
        WhatItDoes:
            "Stops Windows Update offering drivers with the monthly updates, so it doesn't replace the GPU and chipset " +
            "drivers you installed (from the Drivers section or the vendor) with older ones. New devices you plug in " +
            "may need their driver installed by hand.",
        Evidence:
            "Microsoft's ExcludeWUDriversInQualityUpdate policy. Windows Update replacing a freshly installed GPU driver " +
            "is a long-standing complaint from NVIDIA and AMD users.",
        Sources: [UpdatePolicies],
        Tier: EvidenceTier.Proven,
        Undo: "Puts back the previous policy value, or removes it if there was none, so Windows Update offers drivers again.",
        Steps: [Policy("ExcludeWUDriversInQualityUpdate", 1)])
    {
        Applicability = BusinessEditions,
    };

    public static TweakDefinition ActiveHours { get; } = new(
        Id: "update.active-hours",
        Section: Sections.WindowsUpdate,
        Name: "Active hours 8 AM to 2 AM",
        WhatItDoes:
            "Sets active hours to 8 AM until 2 AM, the longest window Windows allows (18 hours), and stops Windows " +
            "adjusting them on its own. Windows won't restart for updates during active hours.",
        Evidence:
            "The same values the Settings app writes under Windows Update > Advanced options > Active hours. Microsoft " +
            "documents an 18-hour maximum.",
        Sources: [Restarts],
        Tier: EvidenceTier.Proven,
        Undo: "Puts back the previous active hours and the automatic adjustment setting.",
        Steps:
        [
            Ux("ActiveHoursStart", RegistryValue.DWord(8)),
            Ux("ActiveHoursEnd", RegistryValue.DWord(2)),
            Ux("SmartActiveHoursState", RegistryValue.DWord(0)),
        ]);

    public static TweakDefinition NoAutoRestart { get; } = new(
        Id: "update.no-auto-restart",
        Section: Sections.WindowsUpdate,
        Name: "Don't restart automatically while someone is signed in",
        WhatItDoes:
            "Updates still download and install automatically, but Windows waits for you to restart while you're " +
            "signed in, so it never restarts in the middle of a game. Restart yourself when Settings asks, or security " +
            "fixes won't finish installing.",
        Evidence:
            "Microsoft's NoAutoRebootWithLoggedOnUsers policy. Microsoft says it only applies with Configure Automatic " +
            "Updates set to 4 (auto download and schedule the install), so this sets both.",
        Sources: [Restarts],
        Tier: EvidenceTier.Proven,
        Undo: "Puts back the previous restart and automatic-update policy values, or removes them if there were none.",
        Steps:
        [
            Au("NoAutoRebootWithLoggedOnUsers", 1),
            Au("AUOptions", 4),
        ]);

    public static TweakDefinition DeliveryOptimizationSharingOff { get; } = new(
        Id: "update.delivery-optimization-sharing-off",
        Section: Sections.WindowsUpdate,
        Name: "Don't share updates with other PCs",
        WhatItDoes:
            "Turns off Delivery Optimization peering: Windows downloads updates only from Microsoft and never uploads " +
            "them to other PCs on your network or the internet, so updates don't use your upload while you play.",
        Evidence:
            "Microsoft's Delivery Optimization download mode 0, \"HTTP only, no peering\".",
        Sources:
        [
            new("Microsoft: Policy CSP - DeliveryOptimization",
                "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-deliveryoptimization"),
        ],
        Tier: EvidenceTier.Situational,
        Undo: "Puts back the previous download mode, or removes the policy if there was none.",
        Steps:
        [
            new RegistryValueStep(
                new(RegistryRoot.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode"),
                RegistryValue.DWord(0)),
        ])
    {
        Applicability = BusinessEditions,
    };

    public static IReadOnlyList<TweakDefinition> All { get; } =
    [
        Pause, Pin24H2, Pin25H2, DeferQuality7, DeferQuality30, ExcludeDrivers, ActiveHours, NoAutoRestart,
        DeliveryOptimizationSharingOff,
    ];

    private static TweakDefinition Pin(string version, int build) => new(
        Id: $"update.pin-{version.ToLowerInvariant()}",
        Section: Sections.WindowsUpdate,
        Name: $"Stay on Windows 11 {version}",
        WhatItDoes:
            $"Keeps this PC on Windows 11 {version}: the yearly feature update doesn't arrive, while the monthly " +
            "security and quality updates keep coming. When Microsoft ends support for this version, switch this off " +
            "so security fixes don't stop.",
        Evidence:
            "Microsoft's TargetReleaseVersion policy, with ProductVersion set to Windows 11 so the PC stays on " +
            "Windows 11. Microsoft documents it for Pro, Enterprise and Education only, so it isn't offered on Home.",
        Sources: [UpdatePolicies],
        Tier: EvidenceTier.Proven,
        Undo: "Puts back the previous target-version policy values, or removes them if there were none.",
        Steps:
        [
            Policy("TargetReleaseVersion", 1),
            new RegistryValueStep(new(RegistryRoot.LocalMachine, Policies, "TargetReleaseVersionInfo"), RegistryValue.String(version)),
            new RegistryValueStep(new(RegistryRoot.LocalMachine, Policies, "ProductVersion"), RegistryValue.String("Windows 11")),
        ])
    {
        // Only the version this PC runs is offered; the others are hidden, not greyed out.
        Applicability = BusinessEditions with { MinBuild = build, MaxBuild = build, Hide = true },
    };

    private static TweakDefinition DeferQuality(uint days) => new(
        Id: $"update.defer-quality-{days}",
        Section: Sections.WindowsUpdate,
        Name: $"Get monthly updates {days} days late",
        WhatItDoes:
            $"Holds each monthly quality update back for {days} days after release, so a bad patch is usually pulled " +
            "or fixed before it reaches you. Updates are never blocked: they still install, just later. 30 days is the " +
            "most the app allows.",
        Evidence:
            "Microsoft's DeferQualityUpdatesPeriodInDays policy (0 to 30 days). Some anti-cheats want a current Windows, " +
            "so keep the delay short if you play Valorant or Call of Duty.",
        Sources: [UpdatePolicies],
        Tier: EvidenceTier.Situational,
        Undo: "Puts back the previous deferral policy values, or removes them if there were none.",
        Steps:
        [
            Policy("DeferQualityUpdates", 1),
            Policy("DeferQualityUpdatesPeriodInDays", days),
        ])
    {
        Applicability = BusinessEditions,
        AntiCheat = OutdatedWindowsConflicts,
    };

    private static UpdateControl.UpdatePauseStep PauseValue(string name, bool isEnd) =>
        new(name, isEnd, UpdateControl.UpdatePauseStep.MaxDays);

    private static RegistryValueStep Ux(string name, RegistryValue value) =>
        new(new(RegistryRoot.LocalMachine, UxSettings, name), value);

    private static RegistryValueStep Policy(string name, uint value) =>
        new(new(RegistryRoot.LocalMachine, Policies, name), RegistryValue.DWord(value));

    private static RegistryValueStep Au(string name, uint value) =>
        new(new(RegistryRoot.LocalMachine, Policies + @"\AU", name), RegistryValue.DWord(value));
}
