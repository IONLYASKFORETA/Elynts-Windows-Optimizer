using Elynt.Core;
using Elynt.Core.Bios;
using Elynt.Core.Drivers;
using Elynt.Core.Journal;
using Elynt.Core.Sensors;
using Elynt.Core.Startup;
using Elynt.Core.Tests;
using Elynt.Core.Tests.Services;
using Elynt.Core.Tests.Startup;
using Elynt.Core.Tweaks;

namespace Elynt.App.Screenshots;

/// <summary>
/// A made-up gaming PC on the test fakes: Windows 11 Pro 24H2, an ASUS B650 board with a Ryzen 7 7800X3D, 32 GB of
/// DDR5 and an RTX 3080, with a few tweaks already on (one waiting for a restart). Nothing is real.
/// </summary>
public static class SamplePc
{
    public const string User = "S-1-5-21-screenshots";

    /// <summary>What the sample PC's owner has switched on already, so Home and the sections have something to count.</summary>
    private static readonly string[] AlreadyOn =
    [
        "gaming.enhance-pointer-precision-off",
        "gaming.game-mode-on",
        "gaming.hags-on", // needs a restart
        "privacy.diagnostic-data-minimum",
        "privacy.tailored-experiences-off",
        "debloat.start-recommendations-off",
        "debloat.bing-in-start-search-off",
        "update.delivery-optimization-sharing-off",
    ];

    public static Engine NewEngine()
    {
        var engine = new Engine(
            Catalogue.Default,
            new SystemAdapters(
                new FakeRegistry(),
                new FakeSystemInfo { OsBuild = "26100.4652" },
                new FakeHardwareInfo { Snapshot = Hardware },
                new FakePackageManager(),
                new FakeRepairSystem(),
                Drivers: Drivers(),
                Games: new FakeGameSources(),
                Cleanup: new FakeCleanupSystem())
            {
                Boot = new FakeBootConfig(),
                Power = new FakePowerPlans(),
                Services = new FakeServiceManager(),
                Startup = new StartupAdapters(new FakeStartupSources(), new FakeScheduledTasks(), new FakeFileSignatures(), new FakeStartupImpactLog()),
                PnpDevices = new FakeDevices(),
                ScanExclusions = new FakeScanExclusions(),
                DefenderState = new FakeDefenderState(),
                Apps = new FakeRemovableApps(),
                Features = new FakeOptionalFeatures(),
                // No PawnIO, so the CPU's own temperature is missing and Home says how to get it.
                Sensors =
                [
                    new FakeSensorSource("NVIDIA NVML",
                        new SensorReading("nvidia0.temperature", "NVIDIA GeForce RTX 3080 temperature", SensorKind.Temperature, 47, "°C"),
                        new SensorReading("nvidia0.clock", "NVIDIA GeForce RTX 3080 clock", SensorKind.Clock, 1710, "MHz"),
                        new SensorReading("nvidia0.power", "NVIDIA GeForce RTX 3080 power", SensorKind.Power, 38.5, "W"),
                        new SensorReading("nvidia0.load", "NVIDIA GeForce RTX 3080 load", SensorKind.Load, 4, "%")),
                    new FakeSensorSource("Windows",
                        new SensorReading(SensorIds.CpuLoad, "CPU load", SensorKind.Load, 6.5, "%"),
                        new SensorReading(SensorIds.CpuClock, "CPU clock", SensorKind.Clock, 4825, "MHz")),
                ],
            },
            new ChangeJournal(new InMemoryJournalStore()),
            User);
        foreach (var id in AlreadyOn)
            engine.Apply(id);
        return engine;
    }

    private static readonly HardwareSnapshot Hardware = new(
        "ASUSTeK COMPUTER INC.", "ROG STRIX B650E-F GAMING WIFI", "AMD Ryzen 7 7800X3D 8-Core Processor",
        [
            new MemoryModule(MemoryType.Ddr5, RatedMts: 4800, ConfiguredMts: 6000) { SizeMb = 16384 },
            new MemoryModule(MemoryType.Ddr5, RatedMts: 4800, ConfiguredMts: 6000) { SizeMb = 16384 },
        ],
        ReadoutState.Unknown);

    /// <summary>The GPU and AMD's chipset driver. No lookups: the tour never presses Check.</summary>
    private static DriverAdapters Drivers()
    {
        var inventory = new FakeDriverInventory
        {
            Adapters = [new DisplayAdapter("NVIDIA GeForce RTX 3080", @"PCI\VEN_10DE&DEV_2206&SUBSYS_38971462", "32.0.15.9174")],
        };
        inventory.ChipsetVersions[GpuVendor.Amd] = "6.10.17.152";
        return new DriverAdapters(
            inventory, [], Path.Combine(Path.GetTempPath(), "elynt-screenshots-downloads"),
            new FakeDownloader(), new FakeSignatureVerifier(), new FakeInstallerRunner());
    }
}
