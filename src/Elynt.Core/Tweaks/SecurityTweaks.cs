using Elynt.Core.Registry;
using Elynt.Core.Security;

namespace Elynt.Core.Tweaks;

/// <summary>
/// The Security section. Every tweak here lowers protection, so each is flagged a security risk and none can go in
/// a per-game profile (the catalogue rejects that). Sources: research/gaming-tweak-catalogue.md #1 and
/// research/anti-cheat-requirements.md rows 1–2.
/// </summary>
public static class SecurityTweaks
{
    private const string HvciKey = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";

    public static TweakDefinition MemoryIntegrityOff { get; } = new(
        Id: "security.memory-integrity-off",
        Section: Sections.Security,
        Name: "Turn off Memory Integrity (core isolation)",
        WhatItDoes:
            "Turns off Memory Integrity, also called hypervisor-protected code integrity or HVCI (Windows Security > " +
            "Device security > Core isolation). It takes effect after a restart. This lowers your PC's security: Memory " +
            "Integrity stops malicious or vulnerable drivers from injecting code into the Windows kernel. Riot Vanguard " +
            "(VALORANT, League of Legends) refuses to start games with it off, and FACEIT can require it too. Turn it " +
            "back on with this same switch.",
        Evidence:
            "Microsoft says Memory Integrity and Virtual Machine Platform can cost gaming performance and suggests " +
            "gamers may turn them off while playing, then back on. Tom's Hardware measured about 3–6% lower average FPS " +
            "with VBS and HVCI on across four CPUs; ComputerBase measured about 6% lower average FPS and 8% lower 1% lows " +
            "in CPU-bound games, from 0% up to about 12% depending on the game. The gain shrinks when the GPU is the limit.",
        Sources:
        [
            new("Microsoft Support: Options to optimize gaming performance in Windows 11 (as quoted by ElevenForum)",
                "https://www.elevenforum.com/t/options-to-optimize-gaming-performance-in-windows-11.9492/"),
            new("Tom's Hardware: Windows 11 VBS and HVCI gaming benchmarks",
                "https://www.tomshardware.com/news/windows-11-gaming-benchmarks-performance-vbs-hvci-security/"),
            new("ComputerBase: Windows 11 VBS/HVCI game benchmarks",
                "https://www.computerbase.de/2023-03/windows-11-vbs-hvci-spiele-benchmarks/"),
            new("Riot Games: VAN: RESTRICTION: 5",
                "https://support.riotgames.com/en-us/riot/penalties/error-van-restriction-5"),
            new("FACEIT: Known issues with Anti-Cheat requirements",
                "https://support.faceit.com/hc/en-us/articles/22851956652956-Known-issues-with-Anti-Cheat-Requirements"),
        ],
        Tier: EvidenceTier.Proven,
        Undo: "Puts back exactly the value that was there before (Memory Integrity back on), then needs a restart.",
        Steps:
        [
            new RegistryValueStep(new(RegistryRoot.LocalMachine, HvciKey, "Enabled"), RegistryValue.DWord(0)),
        ])
    {
        Risks = RiskFlags.SecurityRisk | RiskFlags.NeedsReboot,
        AntiCheat =
        [
            new AntiCheatConflict(AntiCheat.Vanguard, AntiCheatSeverity.WontLaunch, "VAN: RESTRICTION: 5"),
            new AntiCheatConflict(AntiCheat.FaceIt, AntiCheatSeverity.WontLaunch,
                "FACEIT may require Memory Integrity for some players"),
        ],
        ProfileEligible = false,
    };

    /// <summary>Excludes one game's folder (the folder its .exe is in) from Defender scans.</summary>
    public static PerGameTweak GameFolderExclusion { get; } = new(
        new TweakDefinition(
            Id: "security.game-folder-scan-exclusion",
            Section: Sections.Security,
            Name: "Exclude a game folder from Microsoft Defender scans",
            WhatItDoes:
                "Adds the folder the game's .exe is in to Microsoft Defender's exclusions, so Defender's real-time and " +
                "scheduled scans stop checking files there while the game loads them. Defender stays on for everything " +
                "else. Warning: a file in an excluded folder isn't scanned, so malware hidden in or dropped into that " +
                "folder (for example by a mod or a cracked download) would not be caught. Only exclude folders of games " +
                "you installed from a launcher you trust.",
            Evidence:
                "Microsoft documents exclusions as a way to reduce scanning overhead for trusted folders, and warns that " +
                "they lower protection. Real-time scanning of game files is a known cause of load-time stutter on slower " +
                "drives; the benefit is mostly shorter loading and fewer hitches while assets stream in, not higher FPS.",
            Sources:
            [
                new("Microsoft Learn: Configure custom exclusions for Microsoft Defender Antivirus",
                    "https://learn.microsoft.com/en-us/defender-endpoint/configure-exclusions-microsoft-defender-antivirus"),
                new("Microsoft Learn: Common mistakes to avoid when defining exclusions",
                    "https://learn.microsoft.com/en-us/defender-endpoint/common-exclusion-mistakes-microsoft-defender-antivirus"),
            ],
            Tier: EvidenceTier.Situational,
            Undo: "Removes the folder from the exclusions again, unless it was already excluded before the app added it.",
            Steps: [])
        {
            Risks = RiskFlags.SecurityRisk,
            ProfileEligible = false,
        },
        exePath => [new Security.ScanExclusionStep(exePath[..exePath.LastIndexOf('\\')])]);

    /// <summary>The Group Policy key Microsoft documents for "Turn off Microsoft Defender Antivirus".</summary>
    private const string DefenderPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows Defender";
    private const string RealTimeKey = DefenderPolicyKey + @"\Real-Time Protection";

    private static RegistryValueStep PolicyOne(string key, string name) =>
        new(new RegistryLocation(RegistryRoot.LocalMachine, key, name), RegistryValue.DWord(1));

    /// <summary>
    /// A manual-only, dControl-style way to turn Microsoft Defender off by writing the documented Group Policy values.
    /// It refuses until the user has turned Tamper Protection off by hand (the app never does that for them, and
    /// can't), and turning it back on is one click: undo puts back exactly the values that were there before. Never
    /// part of a per-game profile.
    /// </summary>
    public static TweakDefinition DefenderOff { get; } = new(
        Id: "security.defender-off",
        Section: Sections.Security,
        Name: "Turn off Microsoft Defender (manual, dControl-style)",
        WhatItDoes:
            "Turns Microsoft Defender's real-time antivirus off by setting the documented Group Policy values Microsoft " +
            "ships for it (the same switch Group Policy and tools like dControl use). This is a big, manual, opt-in " +
            "change that lowers your PC's protection: with Defender off, nothing is scanning files you open or download " +
            "unless you have installed another antivirus. Windows won't let any program change Defender while Tamper " +
            "Protection is on, so this stays greyed out, with a guide, until you turn Tamper Protection off by hand in " +
            "Windows Security. Turn Defender back on here with one click; that restores the exact settings that were " +
            "there before, and doesn't need Tamper Protection off.",
        Evidence:
            "Microsoft documents the \"Turn off Microsoft Defender Antivirus\" policy and the Real-Time Protection " +
            "policy values. Turning Defender off is a real security trade-off, not a performance tweak with a measured " +
            "FPS gain: it is here only for people who have chosen to run another antivirus or who know what they are " +
            "doing. Tamper Protection must be turned off by hand first; the app never touches it.",
        Sources:
        [
            new("Microsoft Learn: Microsoft Defender Antivirus Group Policy settings (Real-Time Protection)",
                "https://learn.microsoft.com/en-us/defender-endpoint/configure-real-time-protection-microsoft-defender-antivirus"),
        ],
        Tier: EvidenceTier.Situational,
        Undo:
            "Turns Microsoft Defender back on by putting back exactly the policy values that were there before (removing " +
            "the ones the app added), then needs a restart. Doesn't need Tamper Protection off.",
        Steps:
        [
            PolicyOne(DefenderPolicyKey, "DisableAntiSpyware"),
            PolicyOne(RealTimeKey, "DisableRealtimeMonitoring"),
            PolicyOne(RealTimeKey, "DisableBehaviorMonitoring"),
            PolicyOne(RealTimeKey, "DisableOnAccessProtection"),
            PolicyOne(RealTimeKey, "DisableScanOnRealtimeEnable"),
        ])
    {
        Risks = RiskFlags.SecurityRisk | RiskFlags.NeedsReboot,
        ApplyWarning =
            "This turns Microsoft Defender's real-time antivirus off. Your PC will have no antivirus scanning unless " +
            "you have installed another one. Only do this if you have chosen to run a different antivirus or you " +
            "understand the risk. You can turn Defender back on here with one click at any time.",
        Guard = TamperProtectionGuard.Instance,
        ProfileEligible = false,
    };

    public static IReadOnlyList<TweakDefinition> All { get; } = [MemoryIntegrityOff, DefenderOff];

    public static IReadOnlyList<PerGameTweak> PerGame { get; } = [GameFolderExclusion];
}
