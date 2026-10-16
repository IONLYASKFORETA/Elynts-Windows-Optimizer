using Elynt.Core.Bios;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>BIOS readouts and guides, driven through the engine with fake hardware.</summary>
public class BiosReportTests
{
    private readonly FakeHardwareInfo _hardware = new();

    private BiosReport Report() => new Engine(
        Catalogue.Default,
        new SystemAdapters(new FakeRegistry(), new FakeSystemInfo(), _hardware),
        new ChangeJournal(new InMemoryJournalStore()),
        "S-1-5-21-test").GetBiosReport();

    private static BiosReadout Readout(BiosReport report, string id) => report.Readouts.Single(r => r.Id == id);

    [Fact]
    public void Memory_running_above_its_rated_speed_shows_xmp_as_on()
    {
        _hardware.Snapshot = _hardware.Snapshot with { Memory = [new(MemoryType.Ddr5, 4800, 6000), new(MemoryType.Ddr5, 4800, 6000)] };

        var xmp = Readout(Report(), BiosReadoutIds.MemoryProfile);

        Assert.Equal(ReadoutState.On, xmp.State);
        Assert.Contains("6000", xmp.Detail);
    }

    [Fact]
    public void Memory_at_its_standard_speed_shows_xmp_as_off()
    {
        _hardware.Snapshot = _hardware.Snapshot with { Memory = [new(MemoryType.Ddr4, 2666, 2666)] };

        Assert.Equal(ReadoutState.Off, Readout(Report(), BiosReadoutIds.MemoryProfile).State);
    }

    [Theory]
    [InlineData(MemoryType.Ddr4, 3600)]
    [InlineData(MemoryType.Ddr5, 7200)]
    public void Memory_faster_than_any_standard_speed_shows_xmp_as_on_even_when_firmware_reports_it_as_the_rating(MemoryType type, int mts)
    {
        _hardware.Snapshot = _hardware.Snapshot with { Memory = [new(type, mts, mts)] };

        Assert.Equal(ReadoutState.On, Readout(Report(), BiosReadoutIds.MemoryProfile).State);
    }

    [Fact]
    public void Memory_that_reports_no_speed_shows_xmp_as_unknown()
    {
        _hardware.Snapshot = _hardware.Snapshot with { Memory = [new(MemoryType.Ddr5, 0, 0)] };

        Assert.Equal(ReadoutState.Unknown, Readout(Report(), BiosReadoutIds.MemoryProfile).State);
    }

    [Fact]
    public void Pbo_does_not_apply_to_an_intel_cpu()
    {
        Assert.Equal(ReadoutState.NotApplicable, Readout(Report(), BiosReadoutIds.Pbo).State);
    }

    [Theory]
    [InlineData(ReadoutState.On)]
    [InlineData(ReadoutState.Off)]
    [InlineData(ReadoutState.Unknown)]
    public void Pbo_on_a_ryzen_shows_what_the_hardware_reports(ReadoutState reported)
    {
        _hardware.Snapshot = AmdBoard("Micro-Star International Co., Ltd.", "MAG B650 TOMAHAWK WIFI (MS-7D75)", "AMD Ryzen 7 7800X3D 8-Core Processor") with { Pbo = reported };

        Assert.Equal(reported, Readout(Report(), BiosReadoutIds.Pbo).State);
    }

    [Fact]
    public void Stability_test_links_are_occt_and_3dmark()
    {
        var tools = Report().StabilityTools;

        Assert.Contains(tools, t => t.Name.StartsWith("OCCT") && t.Url == "https://www.ocbase.com/");
        Assert.Contains(tools, t => t.Name.StartsWith("3DMark") && t.Url.StartsWith("https://"));
    }

    [Fact]
    public void An_asus_board_with_a_13th_gen_intel_cpu_gets_asus_xmp_steps_and_the_intel_undervolt_guide()
    {
        var guides = Report().Guides;

        Assert.Equal([BiosGuideIds.MemoryProfile, BiosGuideIds.IntelUndervolt], guides.Select(g => g.Id));
        var memory = guides[0];
        Assert.Contains("XMP", memory.Title);
        Assert.Contains(memory.Steps, s => s.Contains("Ai Overclock Tuner"));
        var undervolt = guides[1];
        Assert.Contains("Undervolt Protection", undervolt.Summary);
        Assert.Contains(undervolt.Steps, s => s.Contains("Ai Tweaker"));
        Assert.Contains("0x12B", undervolt.Summary);
    }

    [Fact]
    public void An_msi_board_with_a_ryzen_7000_gets_msi_expo_pbo_and_curve_optimizer_guides()
    {
        _hardware.Snapshot = AmdBoard("Micro-Star International Co., Ltd.", "MAG B650 TOMAHAWK WIFI (MS-7D75)", "AMD Ryzen 7 7800X3D 8-Core Processor");

        var guides = Report().Guides;

        Assert.Equal([BiosGuideIds.MemoryProfile, BiosGuideIds.Pbo, BiosGuideIds.CurveOptimizer], guides.Select(g => g.Id));
        Assert.Contains("EXPO", guides[0].Title);
        Assert.Contains(guides[0].Steps, s => s.Contains("OC"));
        Assert.Contains(guides[1].Steps, s => s.Contains("AMD Overclocking"));
        Assert.Contains(guides[2].Steps, s => s.Contains("-30"));
    }

    [Fact]
    public void A_ryzen_3000_gets_no_curve_optimizer_guide()
    {
        _hardware.Snapshot = AmdBoard("Gigabyte Technology Co., Ltd.", "B450 AORUS ELITE", "AMD Ryzen 5 3600 6-Core Processor") with
        {
            Memory = [new(MemoryType.Ddr4, 2133, 2133)],
        };

        var guides = Report().Guides;

        Assert.Equal([BiosGuideIds.MemoryProfile, BiosGuideIds.Pbo], guides.Select(g => g.Id));
        Assert.Contains("Tweaker", string.Join(" ", guides[0].Steps));
    }

    [Fact]
    public void An_older_intel_cpu_gets_the_plundervolt_explanation()
    {
        _hardware.Snapshot = _hardware.Snapshot with { BoardManufacturer = "ASRock", BoardProduct = "Z390 Phantom Gaming 4", CpuName = "Intel(R) Core(TM) i9-9900K CPU @ 3.60GHz" };

        var undervolt = Report().Guides.Single(g => g.Id == BiosGuideIds.IntelUndervolt);

        Assert.Contains("Plundervolt", undervolt.Summary);
        Assert.Contains(undervolt.Steps, s => s.Contains("OC Tweaker"));
    }

    [Fact]
    public void A_prebuilt_from_a_pc_maker_gets_generic_steps_that_warn_the_setting_may_be_locked()
    {
        _hardware.Snapshot = _hardware.Snapshot with { BoardManufacturer = "Dell Inc.", BoardProduct = "0XYZ12" };

        var memory = Report().Guides.Single(g => g.Id == BiosGuideIds.MemoryProfile);

        Assert.Contains("locked", string.Join(" ", memory.Steps));
    }

    [Fact]
    public void Every_guide_ends_with_a_stability_test_and_cites_a_source()
    {
        _hardware.Snapshot = AmdBoard("ASUSTeK COMPUTER INC.", "TUF GAMING X670E-PLUS", "AMD Ryzen 9 9950X 16-Core Processor");

        Assert.All(Report().Guides, g =>
        {
            Assert.Contains("OCCT", g.Steps[^1]);
            Assert.NotEmpty(g.Sources);
        });
    }

    private static HardwareSnapshot AmdBoard(string vendor, string board, string cpu) =>
        new(vendor, board, cpu, [new(MemoryType.Ddr5, 4800, 6000)], ReadoutState.Unknown);

    [Fact]
    public void Hardware_that_cannot_be_read_still_gives_a_report_with_unknown_readouts()
    {
        _hardware.Fail = new InvalidOperationException("SMBIOS unavailable.");

        var report = Report();

        Assert.All(report.Readouts, r => Assert.Equal(ReadoutState.Unknown, r.State));
        Assert.NotEmpty(report.StabilityTools);
    }
}
