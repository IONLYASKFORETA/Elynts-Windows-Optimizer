namespace Elynt.Core.Tweaks;

/// <summary>What undo can and cannot do for a tweak.</summary>
public enum Reversibility
{
    /// <summary>Undo puts back the exact prior state from the journal.</summary>
    Reversible,
    /// <summary>Undo works, but has to download something again (e.g. reinstalling a removed app).</summary>
    NeedsDownload,
    /// <summary>Undo cannot put it back. Applying needs an explicit confirmation.</summary>
    Destructive,
}

/// <summary>Risks shown on every tweak they apply to. Anti-cheat conflicts are listed separately, by anti-cheat.</summary>
[Flags]
public enum RiskFlags
{
    None = 0,
    /// <summary>Lowers the PC's protection.</summary>
    SecurityRisk = 1,
    /// <summary>Only takes effect after a restart; shows "pending reboot" until then.</summary>
    NeedsReboot = 2,
    /// <summary>Can leave the PC unable to boot.</summary>
    CanStopBoot = 4,
}

/// <summary>Anti-cheats named in research/anti-cheat-requirements.md.</summary>
public enum AntiCheat
{
    Vanguard,
    FaceIt,
    EasyAntiCheat,
    BattlEye,
    Ricochet,
    Javelin,
}

/// <summary>How badly an anti-cheat objects, on the research matrix's scale. Warns, never blocks.</summary>
public enum AntiCheatSeverity
{
    /// <summary>The anti-cheat stops the other program or driver from loading; the game still runs.</summary>
    ToolBlocked,
    /// <summary>Removed from a session, no account penalty.</summary>
    Kick,
    /// <summary>The game runs with limited modes or matchmaking.</summary>
    Restricted,
    /// <summary>The anti-cheat refuses to start the game.</summary>
    WontLaunch,
}

/// <summary>One anti-cheat that objects to a tweak. Installed-game naming builds on this.</summary>
/// <param name="Detail">The error or rule the anti-cheat shows, e.g. "VAN: RESTRICTION: 5".</param>
public sealed record AntiCheatConflict(AntiCheat AntiCheat, AntiCheatSeverity Severity, string Detail);

/// <summary>Hardware facts a tweak can require. Detected by <see cref="ISystemInfo"/>.</summary>
[Flags]
public enum HardwareTraits
{
    None = 0,
    Laptop = 1 << 0,
    NvidiaGpu = 1 << 1,
    AmdGpu = 1 << 2,
    IntelGpu = 1 << 3,
    AmdCpu = 1 << 4,
    IntelCpu = 1 << 5,
}

/// <summary>Windows 11 editions, from the EditionID registry value.</summary>
public enum WindowsEdition
{
    Unknown,
    Home,
    Pro,
    Education,
    Enterprise,
}

/// <summary>When a tweak fits this PC. Everything left unset means "any".</summary>
/// <param name="MinBuild">Lowest Windows build (e.g. 26100) the tweak works on.</param>
/// <param name="MaxBuild">Highest Windows build the tweak works on.</param>
/// <param name="Editions">Editions the tweak works on; empty means all.</param>
/// <param name="RequiresAll">Hardware the PC must have, all of it.</param>
/// <param name="RequiresAny">Hardware the PC must have at least one of (e.g. any GPU vendor); None means no requirement.</param>
/// <param name="Hide">Hide the tweak instead of showing it as not applicable.</param>
public sealed record Applicability(
    int? MinBuild = null,
    int? MaxBuild = null,
    IReadOnlyList<WindowsEdition>? Editions = null,
    HardwareTraits RequiresAll = HardwareTraits.None,
    HardwareTraits RequiresAny = HardwareTraits.None,
    bool Hide = false)
{
    public static Applicability Always { get; } = new();

    /// <summary>Why the tweak does not fit this PC, or null when it does.</summary>
    public string? WhyNot(ISystemInfo system)
    {
        var build = system.BuildNumber;
        if (MinBuild is { } min && build < min)
            return $"Needs Windows build {min} or newer; this PC has {build}.";
        if (MaxBuild is { } max && build > max)
            return $"Not supported after Windows build {max}; this PC has {build}.";
        if (Editions is { Count: > 0 } editions && !editions.Contains(system.Edition))
            return $"Needs Windows 11 {string.Join(" or ", editions)}; this PC has {system.Edition}.";
        var missing = RequiresAll & ~system.Hardware;
        if (missing != HardwareTraits.None)
            return $"Needs hardware this PC doesn't have: {Describe(missing)}.";
        if (RequiresAny != HardwareTraits.None && (RequiresAny & system.Hardware) == HardwareTraits.None)
            return $"Needs one of: {Describe(RequiresAny)}.";
        return null;
    }

    private static string Describe(HardwareTraits traits) => string.Join(", ",
        Enum.GetValues<HardwareTraits>().Where(t => t != HardwareTraits.None && traits.HasFlag(t)).Select(t => t switch
        {
            HardwareTraits.Laptop => "a laptop",
            HardwareTraits.NvidiaGpu => "an NVIDIA GPU",
            HardwareTraits.AmdGpu => "an AMD GPU",
            HardwareTraits.IntelGpu => "an Intel GPU",
            HardwareTraits.AmdCpu => "an AMD CPU",
            HardwareTraits.IntelCpu => "an Intel CPU",
            _ => t.ToString(),
        }));
}
