using Elynt.Core.Bios;
using Elynt.Core.Drivers;
using Elynt.Core.Journal;
using Elynt.Core.Overview;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>The PC overview the Home page shows: this PC's hardware and Windows, and how far its tweaks have gone.</summary>
public class PcOverviewTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new() { OsBuild = "26100.4652" };
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Counts_the_tweaks_that_are_on_out_of_those_that_fit_this_PC()
    {
        var on = Tweak("test.on");
        var off = Tweak("test.off");
        var newerBuild = Tweak("test.newer-build") with { Applicability = new Applicability(MinBuild: 27000) };
        var engine = EngineFor(on, off, newerBuild);
        engine.Apply(on.Id);

        var overview = engine.GetPcOverview();

        Assert.Equal(1, overview.TweaksOn);
        Assert.Equal(2, overview.TweaksApplicable);
    }

    [Fact]
    public void A_tweak_that_does_not_fit_this_PC_is_left_out_even_when_its_setting_is_in_place()
    {
        var proOnly = Tweak("test.pro-only") with { Applicability = new Applicability(Editions: [WindowsEdition.Pro]) };
        var engine = EngineFor(proOnly, Tweak("test.plain"));
        engine.Apply(proOnly.Id);
        _system.Edition = WindowsEdition.Home;

        var overview = engine.GetPcOverview();

        Assert.Equal(0, overview.TweaksOn);
        Assert.Equal(1, overview.TweaksApplicable);
    }

    [Fact]
    public void Counts_the_reboot_needing_changes_made_since_Windows_last_started()
    {
        var first = Tweak("test.reboot-first") with { Risks = RiskFlags.NeedsReboot };
        var second = Tweak("test.reboot-second") with { Risks = RiskFlags.NeedsReboot };
        var beforeBoot = Tweak("test.reboot-before-boot") with { Risks = RiskFlags.NeedsReboot };
        var instant = Tweak("test.instant");
        var engine = EngineFor(first, second, beforeBoot, instant);
        engine.Apply(beforeBoot.Id);
        _clock.Advance(TimeSpan.FromMinutes(5));
        _system.BootTime = _clock.GetUtcNow();
        _clock.Advance(TimeSpan.FromMinutes(5));
        engine.Apply(first.Id);
        engine.Apply(second.Id);
        engine.Apply(instant.Id);

        var overview = engine.GetPcOverview();

        Assert.Equal(2, overview.PendingReboots);
    }

    [Fact]
    public void Counts_each_section_in_sidebar_order_leaving_out_tweaks_that_do_not_fit()
    {
        var gamingOn = Tweak("test.gaming-on");
        var securityOn = Tweak("test.security-on", Sections.Security);
        var securityHomeOnly = Tweak("test.security-home-only", Sections.Security) with
        {
            Applicability = new Applicability(Editions: [WindowsEdition.Home]),
        };
        var engine = EngineFor(gamingOn, Tweak("test.gaming-off"), Tweak("test.gaming-also-off"), securityOn, securityHomeOnly,
            Tweak("test.cleanup-off", Sections.Cleanup));
        engine.Apply(gamingOn.Id);
        engine.Apply(securityOn.Id);

        var sections = engine.GetPcOverview().Sections;

        Assert.Equal(Sections.All, sections.Select(s => s.Section));
        Assert.Equal(new SectionCount(Sections.Gaming, On: 1, Total: 3), sections.Single(s => s.Section == Sections.Gaming));
        Assert.Equal(new SectionCount(Sections.Security, On: 1, Total: 1), sections.Single(s => s.Section == Sections.Security));
        Assert.Equal(new SectionCount(Sections.Cleanup, On: 0, Total: 1), sections.Single(s => s.Section == Sections.Cleanup));
        // A section of tools has no switches to count.
        Assert.Equal(new SectionCount(Sections.Repair, On: 0, Total: 0), sections.Single(s => s.Section == Sections.Repair));
    }

    [Fact]
    public void A_per_game_tweak_counts_once_for_each_game_it_is_set_for_and_not_before()
    {
        var perGame = new PerGameTweak(Tweak("test.per-game"), exe =>
            [new RegistryValueStep(new RegistryLocation(RegistryRoot.CurrentUser, @"Software\ElyntTest\Games", exe), RegistryValue.DWord(1))]);
        var engine = new Engine(new Catalogue([], [perGame]), new SystemAdapters(_registry, _system), _journal, "S-1-5-21-test", clock: _clock);
        var before = engine.GetPcOverview();

        engine.Apply(perGame.IdFor(@"C:\Games\Game\game.exe"));
        var after = engine.GetPcOverview();

        Assert.Equal((0, 0), (before.TweaksOn, before.TweaksApplicable));
        Assert.Equal((1, 1), (after.TweaksOn, after.TweaksApplicable));
    }

    [Fact]
    public void Describes_the_board_CPU_GPUs_and_memory()
    {
        var hardware = new FakeHardwareInfo
        {
            Snapshot = new("ASUSTeK COMPUTER INC.", "ROG STRIX B650E-F GAMING WIFI", "AMD Ryzen 7 7800X3D 8-Core Processor",
                [new MemoryModule(MemoryType.Ddr5, 4800, 6000) { SizeMb = 16384 }, new MemoryModule(MemoryType.Ddr5, 4800, 6000) { SizeMb = 16384 }],
                ReadoutState.Unknown),
        };
        var gpus = new FakeDriverInventory
        {
            Adapters =
            [
                new DisplayAdapter("NVIDIA GeForce RTX 4070 SUPER", @"PCI\VEN_10DE&DEV_2783", "32.0.15.9174"),
                new DisplayAdapter("AMD Radeon(TM) Graphics", @"PCI\VEN_1002&DEV_164E", "31.0.24033.1003"),
            ],
        };
        var engine = EngineOn(new SystemAdapters(_registry, _system, hardware, Drivers: Drivers(gpus)));

        var pc = engine.GetPcOverview().Hardware;

        Assert.Equal("ASUSTeK COMPUTER INC. ROG STRIX B650E-F GAMING WIFI", pc.Board);
        Assert.Equal("AMD Ryzen 7 7800X3D 8-Core Processor", pc.Cpu);
        Assert.Equal(["NVIDIA GeForce RTX 4070 SUPER", "AMD Radeon(TM) Graphics"], pc.Gpus);
        Assert.Equal(32768, pc.MemoryMb);
    }

    [Fact]
    public void Hardware_that_cannot_be_read_shows_as_unknown_and_the_counts_still_come_back()
    {
        var hardware = new FakeHardwareInfo { Fail = new IOException("No SMBIOS table.") };
        var engine = EngineOn(new SystemAdapters(_registry, _system, hardware));

        var overview = engine.GetPcOverview();

        Assert.Null(overview.Hardware.Board);
        Assert.Null(overview.Hardware.Cpu);
        Assert.Null(overview.Hardware.MemoryMb);
        Assert.Empty(overview.Hardware.Gpus);
        Assert.Equal(1, overview.TweaksApplicable);
    }

    [Theory]
    [InlineData("22631.4317", "23H2")]
    [InlineData("26100.4652", "24H2")]
    [InlineData("26200.6899", "25H2")]
    [InlineData("27965.1000", null)] // an Insider build with no version name yet
    public void Names_the_Windows_edition_version_and_build(string build, string? version)
    {
        _system.OsBuild = build;
        _system.Edition = WindowsEdition.Pro;

        var windows = EngineFor(Tweak("test.plain")).GetPcOverview().Windows;

        Assert.Equal(new WindowsRelease(WindowsEdition.Pro, version, build), windows);
    }

    // ---- helpers ----

    private Engine EngineOn(SystemAdapters system) =>
        new(new Catalogue([Tweak("test.plain")]), system, _journal, "S-1-5-21-test", clock: _clock);

    private static DriverAdapters Drivers(IDriverInventory inventory) =>
        new(inventory, [], Path.GetTempPath(), new FakeDownloader(), new FakeSignatureVerifier(), new FakeInstallerRunner());

    /// <summary>A Gaming tweak with a registry value of its own, so each one is on or off by itself.</summary>
    private static TweakDefinition Tweak(string id, string section = Sections.Gaming) =>
        LabelTests.TestTweak(id, new RegistryValueStep(new RegistryLocation(RegistryRoot.CurrentUser, @"Software\ElyntTest", id), RegistryValue.DWord(1)))
            with { Section = section };

    private Engine EngineFor(params TweakDefinition[] tweaks) =>
        new(new Catalogue(tweaks), new SystemAdapters(_registry, _system), _journal, "S-1-5-21-test", clock: _clock);
}
