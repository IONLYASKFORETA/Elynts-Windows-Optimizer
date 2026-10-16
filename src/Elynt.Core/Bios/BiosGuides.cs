using Elynt.Core.Tweaks;

namespace Elynt.Core.Bios;

public static class BiosGuideIds
{
    public const string MemoryProfile = "bios.guide.memory-profile";
    public const string Pbo = "bios.guide.pbo";
    public const string CurveOptimizer = "bios.guide.curve-optimizer";
    public const string IntelUndervolt = "bios.guide.intel-undervolt";
}

/// <summary>
/// Step-by-step BIOS guides for the detected board vendor and CPU. Menu names are those the vendors' recent
/// BIOSes use; they move between versions, so each guide says where else to look.
/// </summary>
public static class BiosGuides
{
    private static readonly Source IntelXmp = new(
        "Intel: how to enable XMP", "https://www.intel.com/content/www/us/en/support/articles/000094616/processors.html");
    private static readonly Source RyzenMasterGuide = new(
        "AMD Ryzen Master user guide: PBO and Curve Optimizer", "https://docs.amd.com/r/en-US/68886-ryzen-master-user-guide/CPU");
    private static readonly Source RyzenMaster = new(
        "AMD: Ryzen Master (PBO, Curve Optimizer and EXPO; warranty note)", "https://www.amd.com/en/products/software/ryzen-master.html");
    private static readonly Source CurveOptimizerFaq = new(
        "AMD: Curve Optimizer FAQ", "https://www.amd.com/content/dam/amd/en/documents/products/software-tools/faq-curve-optimizer.pdf");
    private static readonly Source UndervoltProtection = new(
        "Intel: Undervolt Protection", "https://www.intel.com/content/www/us/en/support/articles/000094219/processors/processor-utilities-and-programs.html");
    private static readonly Source Plundervolt = new(
        "Intel security advisory INTEL-SA-00289 (Plundervolt)", "https://www.intel.com/content/www/us/en/security-center/advisory/intel-sa-00289.html");
    private static readonly Source RaptorLakeInstability = new(
        "Intel: 13th and 14th gen desktop instability root cause", "https://community.intel.com/t5/Blogs/Tech-Innovation/Client/Intel-Core-13th-and-14th-Gen-Desktop-Instability-Root-Cause/post/1633239");

    private const string EnterBios =
        "Restart the PC and keep tapping Delete (or F2) as it starts, until the BIOS opens.";
    private const string SaveAndExit =
        "Press F10, choose Yes to save and exit. If the PC fails to start three times, most boards go back to the previous settings on their own; if it doesn't, clear CMOS as the board's manual describes.";
    private const string StabilityTest =
        "Back in Windows, test before trusting it: run OCCT's CPU and Memory tests for an hour each, then a few 3DMark Time Spy runs. Any error, crash or freeze means undo the change.";

    public static IReadOnlyList<BiosGuide> For(HardwareSnapshot hardware, Cpu cpu)
    {
        var vendor = Boards.Vendor(hardware.BoardManufacturer);
        var guides = new List<BiosGuide> { MemoryProfile(vendor, cpu, MemoryTypeOf(hardware)) };
        if (cpu.Maker == CpuMaker.Amd)
        {
            guides.Add(Pbo(vendor, cpu));
            if (cpu.RyzenSeries >= 5)
                guides.Add(CurveOptimizer(vendor, cpu));
        }
        else if (cpu.Maker == CpuMaker.Intel)
        {
            guides.Add(IntelUndervolt(vendor, cpu));
        }
        return guides;
    }

    private static MemoryType MemoryTypeOf(HardwareSnapshot hardware) =>
        hardware.Memory.Select(m => m.Type).FirstOrDefault(t => t != MemoryType.Other);

    private static BiosGuide MemoryProfile(BoardVendor vendor, Cpu cpu, MemoryType memory)
    {
        var profile = ProfileName(vendor, cpu, memory);
        var where = vendor switch
        {
            BoardVendor.Asus => $"Press F7 for Advanced Mode, open the Ai Tweaker tab and set Ai Overclock Tuner to {profile} I.",
            BoardVendor.Msi => $"Press F7 for Advanced mode, open OC and set the memory profile setting ({profile}) to Profile 1. EZ Mode also has an {profile} button at the top.",
            BoardVendor.Gigabyte => $"Press F2 for Advanced Mode, open the Tweaker tab and set Extreme Memory Profile ({profile}) to Profile1.",
            BoardVendor.AsRock => $"Press F6 for Advanced Mode, open OC Tweaker > DRAM Configuration and set Load {profile} Setting to Profile 1.",
            _ => $"Look for a setting named {profile}, XMP, EXPO or Memory Profile, usually under Overclocking, Performance or Memory. Many PC makers (Dell, HP, Lenovo and others) hide it; if it isn't there, it is locked on this PC and can't be turned on.",
        };
        return new BiosGuide(
            BiosGuideIds.MemoryProfile,
            $"Turn on {profile} so memory runs at its advertised speed",
            $"Memory runs at a slow standard speed until its {profile} profile is turned on. The profile is the speed printed on the kit's box, tested by the maker, and it's the biggest safe BIOS gain for games. Turning it off again puts the standard speed back.",
            [
                EnterBios,
                where,
                "If the board shows more than one profile, pick the first; it is the kit's rated speed.",
                SaveAndExit,
                StabilityTest,
            ],
            cpu.Maker == CpuMaker.Amd ? [RyzenMaster, IntelXmp] : [IntelXmp]);
    }

    private static string ProfileName(BoardVendor vendor, Cpu cpu, MemoryType memory) => cpu.Maker switch
    {
        CpuMaker.Amd when memory == MemoryType.Ddr5 => "EXPO",
        CpuMaker.Amd when vendor == BoardVendor.Asus => "D.O.C.P.",
        CpuMaker.Amd when vendor == BoardVendor.Msi => "A-XMP",
        CpuMaker.Amd => "XMP",
        CpuMaker.Intel => "XMP",
        _ => "XMP / EXPO",
    };

    private static string PboMenu(BoardVendor vendor) => vendor switch
    {
        BoardVendor.Asus => "Press F7 for Advanced Mode, open Ai Tweaker > Precision Boost Overdrive (on some boards it is under Advanced > AMD Overclocking).",
        BoardVendor.Msi => "Press F7 for Advanced mode, open OC > Advanced CPU Configuration > AMD Overclocking, accept the warning, then open Precision Boost Overdrive.",
        BoardVendor.Gigabyte => "Press F2 for Advanced Mode, open Tweaker > Advanced CPU Settings > Precision Boost Overdrive (on some boards it is under Settings > AMD Overclocking).",
        BoardVendor.AsRock => "Press F6 for Advanced Mode, open Advanced > AMD Overclocking, accept the warning, then open Precision Boost Overdrive.",
        _ => "Look under Advanced > AMD Overclocking > Precision Boost Overdrive. Many prebuilt PCs don't offer it; if it isn't there, it is locked on this PC.",
    };

    private static string X3DNote(Cpu cpu) => cpu is { IsX3D: true, RyzenSeries: 5 }
        ? " AMD locked PBO on the Ryzen 7 5800X3D, so the option may be missing or have no effect."
        : "";

    private static BiosGuide Pbo(BoardVendor vendor, Cpu cpu) => new(
        BiosGuideIds.Pbo,
        "Turn on Precision Boost Overdrive (PBO)",
        "PBO lets the CPU boost longer by using the board's power limits instead of AMD's stock ones. It adds heat for a few percent in CPU-heavy games, and AMD says using it voids the CPU's warranty." + X3DNote(cpu),
        [
            EnterBios,
            PboMenu(vendor),
            "Set Precision Boost Overdrive to Enabled. Leave PBO Scalar and Max CPU Boost Clock Override on Auto.",
            SaveAndExit,
            "Watch temperatures under load (for example in OCCT's monitoring tab). If the CPU sits at its temperature limit all the time, PBO gains little on this cooler; set it back to Auto.",
            StabilityTest,
        ],
        [RyzenMasterGuide, RyzenMaster]);

    private static BiosGuide CurveOptimizer(BoardVendor vendor, Cpu cpu) => new(
        BiosGuideIds.CurveOptimizer,
        "Undervolt with Curve Optimizer",
        "Curve Optimizer lowers the voltage the CPU asks for at each speed. A negative offset runs cooler and often boosts higher. Too much makes the PC crash or reboot at idle, which is why you test each step." + X3DNote(cpu),
        [
            EnterBios,
            PboMenu(vendor),
            "Open Curve Optimizer, set it to All Cores, set the sign to Negative and the magnitude to 10.",
            SaveAndExit,
            "If a full stability test passes, go back and lower it by 5 at a time, testing after each step. Never go past -30. At the first error, crash or random reboot, go back to the last value that passed.",
            StabilityTest,
        ],
        [CurveOptimizerFaq, RyzenMasterGuide]);

    private static BiosGuide IntelUndervolt(BoardVendor vendor, Cpu cpu)
    {
        var generation = cpu.IntelGeneration;
        var why = generation switch
        {
            >= 12 => "On 12th gen and newer, Intel's Undervolt Protection is on by default: voltage offsets below the boot voltage are not applied, which is why undervolting from Windows (and this app) doesn't work. Only some K, KF and KS CPUs on Z-series boards can turn it off, in the BIOS, and Intel advises against it.",
            >= 6 => "Since the Plundervolt security fix (2019), most BIOS updates for 6th to 11th gen CPUs lock voltage control. If the voltage offset is greyed out or has no effect, it is locked on this PC.",
            _ => "Undervolting lowers the voltage the CPU runs at, so it runs cooler. Many boards and BIOS updates lock it.",
        };
        if (generation is 13 or 14)
            why += " On 13th and 14th gen desktop CPUs, first update the BIOS to one with Intel microcode 0x12B or newer and choose the Intel Default Settings profile: that fixes the instability Intel confirmed on these chips, and matters more than any undervolt.";

        var where = vendor switch
        {
            BoardVendor.Asus => "Press F7 for Advanced Mode, open Ai Tweaker and set CPU Core/Cache Voltage to Adaptive Mode, then set Offset Mode Sign to \"-\".",
            BoardVendor.Msi => "Press F7 for Advanced mode, open OC and set CPU Core Voltage Mode to Adaptive + Offset, with a \"-\" offset.",
            BoardVendor.Gigabyte => "Press F2 for Advanced Mode, open Tweaker > Advanced Voltage Settings and set a negative CPU Vcore offset (Dynamic Vcore).",
            BoardVendor.AsRock => "Press F6 for Advanced Mode, open OC Tweaker > Voltage Configuration and set CPU Vcore Voltage to Offset Mode with a negative offset.",
            _ => "Look for CPU Core Voltage Offset under Overclocking or Performance. Most prebuilt PCs lock it; if it isn't there, it can't be changed on this PC.",
        };

        List<Source> sources = generation >= 12 ? [UndervoltProtection] : [Plundervolt, UndervoltProtection];
        if (generation is 13 or 14)
            sources.Add(RaptorLakeInstability);

        return new BiosGuide(
            BiosGuideIds.IntelUndervolt,
            "Undervolt an Intel CPU in the BIOS",
            why,
            [
                EnterBios,
                where,
                "Start with an offset of -0.025 V (25 mV).",
                SaveAndExit,
                "If a full stability test passes, lower it by 0.010 V at a time, testing after each step. Don't go past -0.050 V. At the first error or crash, go back to the last value that passed.",
                StabilityTest,
            ],
            sources);
    }
}
