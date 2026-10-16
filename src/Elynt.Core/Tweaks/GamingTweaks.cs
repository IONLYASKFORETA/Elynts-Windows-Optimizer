using Elynt.Core.Registry;

namespace Elynt.Core.Tweaks;

public static class GamingTweaks
{
    private const string MouseKey = @"Control Panel\Mouse";

    public static TweakDefinition EnhancePointerPrecisionOff { get; } = new(
        Id: "gaming.enhance-pointer-precision-off",
        Section: Sections.Gaming,
        Name: "Turn off Enhance pointer precision",
        WhatItDoes:
            "Turns off Windows mouse acceleration, so the same hand movement always moves the cursor the same distance, " +
            "however fast you move. Games that use the Windows cursor get 1:1 aim. Games that read raw mouse input are " +
            "not affected either way. The desktop cursor may feel slower at first.",
        Evidence:
            "Microsoft documents that cursor messages (WM_MOUSEMOVE) have pointer acceleration applied while raw input " +
            "(WM_INPUT) has none. Acceleration depends on speed, so the same movement lands in different places, which " +
            "works against muscle memory. The benefit is consistent aim, not more FPS.",
        Sources:
        [
            new("Microsoft: Taking advantage of high-definition mouse movement",
                "https://learn.microsoft.com/en-us/windows/win32/dxtecharts/taking-advantage-of-high-dpi-mouse-movement"),
            new("How-To Geek: What is Enhance Pointer Precision in Windows?",
                "https://www.howtogeek.com/321763/what-is-enhance-pointer-precision-in-windows/"),
        ],
        Tier: EvidenceTier.Proven,
        Undo: "Puts back exactly the values that were there before. Windows' own defaults are 1, 6 and 10.",
        Steps:
        [
            new RegistryValueStep(new(RegistryRoot.CurrentUser, MouseKey, "MouseSpeed"), RegistryValue.String("0")),
            new RegistryValueStep(new(RegistryRoot.CurrentUser, MouseKey, "MouseThreshold1"), RegistryValue.String("0")),
            new RegistryValueStep(new(RegistryRoot.CurrentUser, MouseKey, "MouseThreshold2"), RegistryValue.String("0")),
        ],
        Refresh: SessionRefresh.MouseSettings)
    {
        Reversibility = Reversibility.Reversible,
        Risks = RiskFlags.None,
        Applicability = Applicability.Always,
        // Instant, but the signed-in session must re-read the mouse settings, which a profile running in the
        // service can't do.
        ProfileEligible = false,
    };

    private static readonly Source X3dParking = new(
        "Hardware Busters: Ryzen 9 7950X3D core parking problem and solution",
        "https://hwbusters.com/cpu/amd-ryzen-9-7950x3d-core-parking-problem-solution/3/");

    private static readonly Source MicrosoftGamingOptions = new(
        "Microsoft Support: Options to optimize gaming performance in Windows 11 (as quoted by ElevenForum)",
        "https://www.elevenforum.com/t/options-to-optimize-gaming-performance-in-windows-11.9492/");

    /// <summary>Dual-CCD X3D CPUs park the non-cache CCD while a game runs; that needs Balanced, Game Mode and Game Bar.</summary>
    public static HardwareNote X3dCoreParking { get; } = new(
        HardwareTraits.DualCcdX3dCpu,
        "Your CPU is a dual-CCD Ryzen X3D. It keeps games on the 3D V-Cache cores by parking the other cores, and that " +
        "only works with the Balanced power plan, Game Mode on and Xbox Game Bar installed. Keep all three, or games " +
        "can lose performance.");

    public static TweakDefinition GameModeOn { get; } = new(
        Id: "gaming.game-mode-on",
        Section: Sections.Gaming,
        Name: "Keep Game Mode on",
        WhatItDoes:
            "Turns on Windows Game Mode (Settings > Gaming > Game Mode). While a game runs, Windows holds back Windows " +
            "Update installs and restart notifications and gives the game priority. It is on by default; this makes sure " +
            "it stays on.",
        Evidence:
            "Microsoft recommends Game Mode on for gaming. Independent results are mixed: a small 2026 MakeUseOf test " +
            "found slightly lower FPS with it on in two of three CPU-bound games and slightly higher in Warzone. On " +
            "dual-CCD Ryzen X3D CPUs it is part of the chain that keeps games on the V-Cache cores, so it matters there. " +
            "When it helps: X3D CPUs, and PCs where updates or notifications interrupt games. Measure if unsure.",
        Sources:
        [
            MicrosoftGamingOptions,
            new("MakeUseOf: I disabled Game Mode and gained back CPU performance",
                "https://www.makeuseof.com/disabled-game-mode-gained-back-cpu-performance-didnt-know-lost"),
            X3dParking,
        ],
        Tier: EvidenceTier.Situational,
        Undo: "Puts back exactly the value that was there before (or removes it if there wasn't one, which Windows reads as on).",
        Steps:
        [
            new RegistryValueStep(new(RegistryRoot.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled"), RegistryValue.DWord(1)),
        ])
    {
        ProfileEligible = true,
        HardwareNotes = [X3dCoreParking],
    };

    /// <summary>DLSS Frame Generation on RTX 40/50 cards needs HAGS on.</summary>
    public static HardwareNote FrameGenerationNeedsHags { get; } = new(
        HardwareTraits.FrameGenerationGpu,
        "Your NVIDIA RTX 40 or 50 series card can use DLSS Frame Generation, which needs hardware-accelerated GPU " +
        "scheduling on. If you use Frame Generation, keep this on: with it off, games grey out the option.");

    public static HardwareNote LaptopPower { get; } = new(
        HardwareTraits.Laptop,
        "On a laptop this uses more power and makes more heat, so the battery drains faster. Consider it only while " +
        "plugged in.");

    public static TweakDefinition HardwareGpuSchedulingOn { get; } = new(
        Id: "gaming.hags-on",
        Section: Sections.Gaming,
        Name: "Turn on hardware-accelerated GPU scheduling (HAGS)",
        WhatItDoes:
            "Lets the graphics card schedule its own work instead of Windows doing it on the CPU (Settings > System > " +
            "Display > Graphics > Hardware-accelerated GPU scheduling). Takes effect after a restart.",
        Evidence:
            "Microsoft describes HAGS as cutting scheduling overhead, without promising more FPS. GamersNexus measured " +
            "differences mostly within 2%, with a small loss in one CPU-limited game. It is required for NVIDIA DLSS " +
            "Frame Generation. When it helps: RTX 40 and 50 series cards using Frame Generation. Otherwise it is " +
            "neutral; try it both ways if a game stutters.",
        Sources:
        [
            new("Microsoft DirectX blog: Hardware-accelerated GPU scheduling",
                "https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/"),
            new("GamersNexus: Hardware-accelerated GPU scheduling benchmarks",
                "https://gamersnexus.net/guides/3599-windows-10-hardware-accelerated-gpu-scheduling-benchmarks"),
            new("igor'sLAB: DLSS 3 Frame Generation needs HAGS",
                "https://www.igorslab.de/en/dlss-3-frame-generierung-in-windows-10-aktivieren/"),
        ],
        Tier: EvidenceTier.Situational,
        Undo: "Puts back exactly the value that was there before, then needs a restart.",
        Steps:
        [
            new RegistryValueStep(
                new(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"),
                RegistryValue.DWord(2)),
        ])
    {
        Risks = RiskFlags.NeedsReboot,
        Applicability = new(RequiresAny: HardwareTraits.NvidiaGpu | HardwareTraits.AmdGpu | HardwareTraits.IntelGpu),
        HardwareNotes = [FrameGenerationNeedsHags],
    };

    public static TweakDefinition GlobalTimerResolution { get; } = new(
        Id: "gaming.global-timer-resolution",
        Section: Sections.Gaming,
        Name: "Make timer resolution requests system-wide",
        WhatItDoes:
            "Since Windows 10 2004, a program that asks for a finer timer (for example 1 ms or 0.5 ms) only gets it for " +
            "itself, and on Windows 11 loses it while its window is minimized or hidden. This puts back the older " +
            "behaviour: any request, from a game or a timer tool, applies to the whole PC. The app doesn't hold a timer " +
            "request itself. Takes effect after a restart.",
        Evidence:
            "Microsoft documents the per-process change in timeBeginPeriod. The measurable effect is how precisely " +
            "Sleep() wakes up, which matters for software frame limiters and some game loops; valleyofdoom's " +
            "MeasureSleep shows it. Microsoft says the registry value is for debugging, and no reputable benchmark " +
            "shows FPS gains. When it helps: frame limiters that rely on Sleep(), with a timer tool running. Most games " +
            "already raise their own timer.",
        Sources:
        [
            new("Microsoft: timeBeginPeriod function",
                "https://learn.microsoft.com/en-us/windows/win32/api/timeapi/nf-timeapi-timebeginperiod"),
            new("valleyofdoom: TimerResolution and MeasureSleep", "https://github.com/valleyofdoom/TimerResolution"),
        ],
        Tier: EvidenceTier.Situational,
        Undo: "Puts back exactly the value that was there before (normally it doesn't exist), then needs a restart.",
        Steps:
        [
            new RegistryValueStep(
                new(RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests"),
                RegistryValue.DWord(1)),
        ])
    {
        Risks = RiskFlags.NeedsReboot,
        HardwareNotes = [LaptopPower],
    };

    public static TweakDefinition WindowedGamesOptimizationsOn { get; } = new(
        Id: "gaming.windowed-games-optimizations-on",
        Section: Sections.Gaming,
        Name: "Keep Optimizations for windowed games on",
        WhatItDoes:
            "Turns on Settings > System > Display > Graphics > Optimizations for windowed games. Older DirectX 10 and 11 " +
            "games running windowed or borderless switch to the modern flip presentation model, which lowers latency and " +
            "lets them use variable refresh rate (G-SYNC, FreeSync) and Auto HDR. Other graphics settings stored next to " +
            "it are left as they are. Games pick it up the next time they start.",
        Evidence:
            "Microsoft lists lower frame latency plus Auto HDR and VRR support as the benefits, and turning on Auto HDR " +
            "forces this setting on. Where Windows stores it (SwapEffectUpgradeEnable in DirectXUserGlobalSettings) is " +
            "not documented by Microsoft; it is what the Settings switch writes on Windows 11. Rarely, one game " +
            "misbehaves with it; it can be turned off for just that game in the same Settings page.",
        Sources:
        [
            new("Microsoft Support: Optimizations for windowed games in Windows 11",
                "https://support.microsoft.com/en-us/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952"),
            new("Microsoft DirectX blog: Updates in graphics and gaming", "https://devblogs.microsoft.com/directx/?p=5822"),
        ],
        Tier: EvidenceTier.Proven,
        Undo:
            "Puts back exactly the value that was there before. If you changed other graphics settings since, those are " +
            "kept and only this one goes back.",
        Steps:
        [
            new RegistryListItemStep(
                new(RegistryRoot.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings"),
                RegistryListFormat.SemicolonPairs, "SwapEffectUpgradeEnable", "1"),
        ]);

    public static IReadOnlyList<TweakDefinition> All { get; } =
    [
        WindowedGamesOptimizationsOn, EnhancePointerPrecisionOff, GameModeOn, HardwareGpuSchedulingOn, GlobalTimerResolution,
    ];
}
