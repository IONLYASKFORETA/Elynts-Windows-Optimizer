namespace Elynt.Core.Drivers;

/// <summary>
/// OEM storage and Wi-Fi drivers that Riot Vanguard and FACEIT AC refuse to launch alongside, because they break DMA
/// remapping. Sources: https://support.riotgames.com/en-us/riot/performance/van-incompatible-oem-driver and
/// https://support.faceit.com/hc/en-us/articles/25296204531996 (see research/anti-cheat-requirements.md, row 11).
/// </summary>
public static class ProblemDrivers
{
    private const string Games = "Valorant and League of Legends (Riot Vanguard) and FACEIT (CS2)";

    /// <param name="FixedIn">The first version that no longer causes trouble, when the sources name one.</param>
    private sealed record Known(string File, string Name, Version? FixedIn = null);

    private static readonly IReadOnlyList<Known> List =
    [
        new("iastorvd", "Intel RST/VMD storage driver"),
        new("secnvme", "Samsung NVMe storage driver"),
        new("solidnvm", "Solidigm NVMe storage driver"),
        new("mtinvme", "Micron NVMe storage driver"),
        new("asstahci64", "ASMedia SATA storage driver"),
        new("mvs91xx", "Marvell SATA storage driver"),
        new("amd_sata", "AMD SATA storage driver"),
        new("mtkwl6ex", "MediaTek RZ616 Wi-Fi driver", new Version(3, 5)),
    ];

    /// <summary>The installed drivers on the list, each with a warning naming the games it stops.</summary>
    public static IReadOnlyList<ProblemDriver> Find(IEnumerable<KernelDriver> installed)
    {
        var problems = new List<ProblemDriver>();
        foreach (var driver in installed)
        {
            var known = List.FirstOrDefault(k => string.Equals(k.File, driver.Name, StringComparison.OrdinalIgnoreCase));
            if (known is null)
                continue;

            if (known.FixedIn is { } fixedIn)
            {
                if (Version.TryParse(driver.FileVersion, out var version) && version >= fixedIn)
                    continue;
                problems.Add(new ProblemDriver(known.Name, known.File + ".sys", driver.FileVersion,
                    $"Versions before {fixedIn} of this driver stop {Games} from launching. Update it to {fixedIn} or later from your PC or Wi-Fi card maker."));
                continue;
            }

            problems.Add(new ProblemDriver(known.Name, known.File + ".sys", driver.FileVersion,
                $"Some versions of this driver stop {Games} from launching. If those games won't start, update it from the maker, or switch the drive to Microsoft's built-in driver."));
        }

        return problems;
    }
}
