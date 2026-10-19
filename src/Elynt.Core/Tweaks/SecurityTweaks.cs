using Elynt.Core.Registry;

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

    public static IReadOnlyList<TweakDefinition> All { get; } = [MemoryIntegrityOff];

    public static IReadOnlyList<PerGameTweak> PerGame { get; } = [GameFolderExclusion];
}
