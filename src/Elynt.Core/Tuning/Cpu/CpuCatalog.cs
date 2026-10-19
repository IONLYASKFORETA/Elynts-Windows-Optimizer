using Elynt.Core.Bios;
using System.Text.RegularExpressions;

namespace Elynt.Core.Tuning.Cpu;

/// <summary>
/// The only SMU messages the app may send to one Ryzen codename. PawnIO's RyzenSMU module passes any message id
/// from 0 to 0xFF, so this list is the only thing between a bug and an arbitrary SMU command.
/// </summary>
/// <param name="SetAllCoreCurve">RSMU "set all-core Curve Optimizer margin", or null where CO isn't supported.</param>
/// <param name="SetPpt">RSMU "set PPT limit", in mW.</param>
/// <param name="SetTdc">RSMU "set TDC limit", in mA.</param>
/// <param name="SetEdc">RSMU "set EDC limit", in mA.</param>
public sealed record SmuAllowlist(string Codename, uint? SetAllCoreCurve, uint SetPpt, uint SetTdc, uint SetEdc)
{
    public IReadOnlySet<uint> Messages { get; } = new HashSet<uint>(
        new[] { SetAllCoreCurve, SetPpt, SetTdc, SetEdc }.OfType<uint>());
}

/// <summary>
/// What the app knows about each CPU: the reviewed Ryzen codenames and their SMU messages, the CPUs where Curve
/// Optimizer is unofficial, and Intel's Default Settings for 13th and 14th gen desktop.
/// Message ids are the publicly documented RSMU ids (Zen 3 and Zen 4/5 families), written down here from public
/// facts; no code was taken from GPL tools. Not yet verified on real hardware.
/// </summary>
public static partial class CpuCatalog
{
    /// <summary>Intel's fix for the 13th/14th gen desktop Vmin shift (idle and light-load voltage), from a BIOS update.</summary>
    public const uint IntelVminFixMicrocode = 0x12B;

    private static readonly SmuAllowlist Vermeer = new("Vermeer", 0x0B, 0x53, 0x54, 0x55);
    private static readonly SmuAllowlist Raphael = new("Raphael", 0x07, 0x56, 0x57, 0x58);
    private static readonly SmuAllowlist GraniteRidge = new("GraniteRidge", 0x07, 0x56, 0x57, 0x58);

    /// <summary>
    /// The reviewed desktop codenames by CPUID family and model: Ryzen 5000, 7000 and 9000, X3D included, as the spec
    /// lists. Ryzen 3000 (Matisse), APUs, mobile and Threadripper parts are left out on purpose.
    /// </summary>
    private static readonly Dictionary<(int Family, int Model), SmuAllowlist> Codenames = new()
    {
        [(0x19, 0x21)] = Vermeer,
        [(0x19, 0x61)] = Raphael,
        [(0x1A, 0x44)] = GraniteRidge,
    };

    /// <returns>The codename's allowlist, or null when the CPU isn't on the reviewed list.</returns>
    public static SmuAllowlist? Allowlist(CpuIdentity cpu) =>
        cpu.Maker == CpuMaker.Amd ? Codenames.GetValueOrDefault((cpu.Family, cpu.Model)) : null;

    /// <summary>
    /// AMD locked Curve Optimizer on the Zen 3 X3D parts (5800X3D, 5700X3D, 5600X3D); it works there only unofficially.
    /// 7000 and 9000 X3D parts support it officially.
    /// </summary>
    public static bool CurveOptimizerUnofficial(CpuIdentity cpu) =>
        Allowlist(cpu) == Vermeer && cpu.Name.Contains("X3D", StringComparison.OrdinalIgnoreCase);

    /// <summary>13th or 14th gen Core desktop (Raptor Lake and its refresh), the parts hit by the Vmin shift.</summary>
    public static bool IsRaptorLakeDesktop(CpuIdentity cpu) =>
        cpu.Maker == CpuMaker.Intel && RaptorDesktop().IsMatch(cpu.Name);

    /// <summary>
    /// Intel Default Settings ("Performance" profile) PL1/PL2 for 13th/14th gen desktop, as reported by the press
    /// (igor'sLAB, 2024); Intel publishes them only to board makers. Null for other CPUs.
    /// </summary>
    public static (int Pl1, int Pl2)? IntelDefaultSettings(CpuIdentity cpu)
    {
        if (!IsRaptorLakeDesktop(cpu) || RaptorDesktop().Match(cpu.Name) is not { Success: true } match)
            return null;
        var tier = match.Groups["tier"].Value;
        var unlocked = match.Groups["suffix"].Value.StartsWith('K');
        return (tier, unlocked) switch
        {
            ("9", true) => (125, 253),
            ("7", true) => (125, 253),
            ("5", true) => (125, 181),
            ("9", false) => (65, 219),
            ("7", false) => (65, 219),
            ("5", false) => (65, 148),
            ("3", false) => (60, 89),
            _ => null,
        };
    }

    // i9-14900K, i7-13700F, i5-13400. Mobile suffixes (H, HX, U, P) don't match, so laptops are left out.
    [GeneratedRegex(@"\bi(?<tier>[3579])-1[34]\d{3}(?<suffix>KS|KF|K|F|T)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex RaptorDesktop();
}
