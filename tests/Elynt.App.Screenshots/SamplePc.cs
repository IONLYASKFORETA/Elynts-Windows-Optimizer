using Elynt.Core;
using Elynt.Core.Journal;
using Elynt.Core.Startup;
using Elynt.Core.Tests;
using Elynt.Core.Tests.Services;
using Elynt.Core.Tests.Startup;
using Elynt.Core.Tweaks;

namespace Elynt.App.Screenshots;

/// <summary>A made-up gaming PC on the test fakes: Windows 11 Pro, an NVIDIA GPU and an AMD CPU. Nothing is real.</summary>
public static class SamplePc
{
    public const string User = "S-1-5-21-screenshots";

    public static Engine NewEngine() => new(
        Catalogue.Default,
        new SystemAdapters(
            new FakeRegistry(),
            new FakeSystemInfo(),
            new FakeHardwareInfo(),
            new FakePackageManager(),
            new FakeRepairSystem(),
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
        },
        new ChangeJournal(new InMemoryJournalStore()),
        User);
}
