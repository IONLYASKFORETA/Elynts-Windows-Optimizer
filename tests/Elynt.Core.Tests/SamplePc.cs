using Elynt.Core.Apps;
using Elynt.Core.Bios;
using Elynt.Core.Drivers;
using Elynt.Core.Journal;
using Elynt.Core.Profiles;
using Elynt.Core.Registry;
using Elynt.Core.Sensors;
using Elynt.Core.Services;
using Elynt.Core.Startup;
using Elynt.Core.Tests.Services;
using Elynt.Core.Tests.Startup;
using Elynt.Core.Tuning;
using Elynt.Core.Tuning.Cpu;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>
/// A made-up gaming PC on the test fakes, for the screenshot tour: Windows 11 Pro 24H2, an ASUS B650 board with a Ryzen 7
/// 7800X3D, 32 GB of DDR5 and an RTX 3080, with a few tweaks already on (one waiting for a restart, one that Windows
/// set by itself, so it is already set and its switch is locked, and one that can't be undone). Every list the app
/// shows has at least two rows and every state a page handles has an example (drift, an undone change, a driver
/// backup), so the tour draws each row template and a binding error in any of them fails CI. Nothing is real.
/// <see cref="SamplePcTests"/> keeps it that way.
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

    /// <summary>Switched on, then undone, so the Undo history has an undone row.</summary>
    public const string Undone = "debloat.lock-screen-tips-off";

    /// <summary>On, then set back by a pretend Windows update, so the Undo history has drift and an undo it refuses.</summary>
    public const string Drifted = "debloat.bing-in-start-search-off";

    /// <summary>
    /// Turned on by Windows itself when the owner switched Auto HDR on, with no change of the app's own: already set,
    /// so Gaming shows its label and its locked switch.
    /// </summary>
    public const string SetByWindows = "gaming.windowed-games-optimizations-on";

    /// <summary>
    /// Confirmed and applied, though it can't be undone: Debloat &amp; Privacy shows its switch locked on, and the Undo
    /// history shows its row with no Undo button.
    /// </summary>
    public const string Destructive = "debloat.deprovision-preinstalled";

    /// <summary>The query the tour types into Apps' winget search.</summary>
    public const string AppSearch = "player";

    private const string DisplayClass = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string Fortnite = @"C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteLauncher.exe";
    private const string Valorant = @"C:\Riot Games\VALORANT\live\VALORANT.exe";
    private const string Discord = @"C:\Users\aden\AppData\Local\Discord\app-1.0.9170\Discord.exe";
    private const string Steam = @"C:\Program Files (x86)\Steam\steam.exe";

    /// <param name="scratch">A folder the sample PC may write to (driver backups and downloads); the caller deletes it.</param>
    public static Engine NewEngine(string scratch)
    {
        var registry = new FakeRegistry();
        var apps = new FakeRemovableApps();
        // What a fresh Windows install provisions for every new account, for the destructive tweak to remove.
        foreach (var app in AppRemovalTweaks.Preinstalled.Prepend(AppRemovalTweaks.Copilot))
            apps.Provision(app.PackageName);
        var (gpuTuning, cpuTuning) = Tuning();
        var engine = new Engine(
            Catalogue.Default,
            new SystemAdapters(
                registry,
                new FakeSystemInfo { OsBuild = "26100.4652" },
                new FakeHardwareInfo { Snapshot = Hardware },
                Packages(),
                new FakeRepairSystem(),
                Drivers: Drivers(scratch),
                Games: Games(),
                Cleanup: Cleanup())
            {
                Boot = new FakeBootConfig(),
                Power = new FakePowerPlans(),
                Services = Services(),
                Startup = Startup(),
                PnpDevices = new FakeDevices(),
                ScanExclusions = new FakeScanExclusions(),
                DefenderState = new FakeDefenderState(),
                Apps = apps,
                Features = new FakeOptionalFeatures(),
                DriverCleaning = DriverCleaning(scratch),
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
            User,
            gpuTuning: gpuTuning,
            cpuTuning: cpuTuning);

        foreach (var id in AlreadyOn)
            Check(engine.Apply(id).Succeeded, $"turning on {id}");
        Check(engine.Apply(Undone).Succeeded && engine.Revert(Undone).Succeeded, $"turning {Undone} on and off");
        Check(engine.Apply(Destructive, new ApplyOptions(ConfirmDestructive: true)).Succeeded, $"confirming and applying {Destructive}");
        // What a Windows update does: writes its own value over each of the tweak's registry values.
        foreach (var step in Catalogue.Default.Get(Drifted).Steps.OfType<RegistryValueStep>())
            registry.SetValue(step.Location, RegistryValue.DWord(0));
        // What Windows writes when Auto HDR is switched on in Settings: windowed games optimizations come on with it.
        foreach (var step in Catalogue.Default.Get(SetByWindows).Steps.OfType<RegistryListItemStep>())
            registry.SetValue(step.Location, RegistryValue.String("SwapEffectUpgradeEnable=1;AutoHDREnable=1;"));

        Check(engine.AddGame(Valorant).Succeeded, "adding VALORANT by its .exe");
        Check(engine.SaveProfile(new GameProfile(Fortnite)
        {
            AppsToClose = [Discord, Steam],
            Priority = GamePriority.High,
            RaiseTimerResolution = true,
        }).Succeeded, "saving Fortnite's profile");
        // A clean from when the PC had an AMD card, so Drivers has a backup to restore.
        Check(engine.CleanDrivers(new DriverCleanRequest(GpuVendor.Amd)).Succeeded, "cleaning the old AMD drivers");
        return engine;
    }

    private static void Check(bool succeeded, string what)
    {
        if (!succeeded)
            throw new InvalidOperationException($"The sample PC failed at {what}.");
    }

    /// <summary>
    /// The RTX 3080 through NVML and the 7800X3D, both on defaults. The real app reaches the CPU only through PawnIO,
    /// which this PC hasn't turned on (for Home's hint); here the CPU answers anyway, so Overclocking draws both
    /// panels' sliders. Nothing disposes these services: the tour never changes a setting, so no countdown ever runs.
    /// </summary>
    private static (GpuTuning Gpu, CpuTuning Cpu) Tuning()
    {
        var safety = new TuningSafety(new InMemoryTuningStore());
        var session = safety.BeginSession();

        var nvml = new FakeNvml();
        nvml.Add("NVIDIA GeForce RTX 3080").Readings = new NvmlReadings(
            TemperatureC: 47, GraphicsClockMHz: 1710, MemoryClockMHz: 9501, PowerMilliwatts: 38_500, UtilizationPercent: 4, FanPercent: 30);
        var gpu = new GpuTuning([new NvidiaGpuTuning(nvml)], safety);
        gpu.RecoverAtStartup(session);

        var processor = FakeCpu.Raphael7800X3D();
        var cpu = new CpuTuning(processor, safety, new InMemoryCpuProfileStore(), processor, processor);
        cpu.RecoverAtStartup(session);
        return (gpu, cpu);
    }

    private static readonly HardwareSnapshot Hardware = new(
        "ASUSTeK COMPUTER INC.", "ROG STRIX B650E-F GAMING WIFI", "AMD Ryzen 7 7800X3D 8-Core Processor",
        [
            new MemoryModule(MemoryType.Ddr5, RatedMts: 4800, ConfiguredMts: 6000) { SizeMb = 16384 },
            new MemoryModule(MemoryType.Ddr5, RatedMts: 4800, ConfiguredMts: 6000) { SizeMb = 16384 },
        ],
        ReadoutState.Unknown);

    /// <summary>The GPU and AMD's chipset driver. No lookups: the tour never presses Check.</summary>
    private static DriverAdapters Drivers(string scratch)
    {
        var inventory = new FakeDriverInventory
        {
            Adapters = [new DisplayAdapter("NVIDIA GeForce RTX 3080", @"PCI\VEN_10DE&DEV_2206&SUBSYS_38971462", "32.0.15.9174")],
        };
        inventory.ChipsetVersions[GpuVendor.Amd] = "6.10.17.152";
        return new DriverAdapters(
            inventory, [], Path.Combine(scratch, "downloads"),
            new FakeDownloader(), new FakeSignatureVerifier(), new FakeInstallerRunner());
    }

    /// <summary>Two storage and Wi-Fi drivers that anti-cheat refuses, and an old AMD card's drivers to clean (see NewEngine).</summary>
    private static DriverCleaningAdapters DriverCleaning(string scratch)
    {
        var store = new FakeDriverStore();
        store.Installed.AddRange(
        [
            new("oem12.inf", "nv_dispi.inf", "NVIDIA", DisplayClass, "32.0.15.9174"),
            new("oem7.inf", "nv_dispi.inf", "NVIDIA", DisplayClass, "32.0.15.6094"),
            new("oem3.inf", "u0403541.inf", "Advanced Micro Devices, Inc.", DisplayClass, "32.0.11037.1004"),
            new("oem4.inf", "u0398863.inf", "Advanced Micro Devices, Inc.", DisplayClass, "31.0.24033.1003"),
        ]);
        var kernel = new FakeKernelDrivers();
        kernel.Drivers.AddRange([new("amd_sata", "1.2.1.401"), new("mtkwl6ex", "3.3.0.908")]);
        return new DriverCleaningAdapters(
            store, kernel, Path.Combine(scratch, "driver-backups"), new FakeDduSource(), Path.Combine(scratch, "ddu"),
            new FakeDownloader(), new FakeSignatureVerifier { Result = new(true, "Wagnardsoft") }, new FakeDduRunner());
    }

    /// <summary>Apps winget lists as installed (two with updates), and players its search finds outside the catalogue.</summary>
    private static FakePackageManager Packages()
    {
        var packages = new FakePackageManager();
        packages.Installed.AddRange(
        [
            new InstalledPackage("Valve.Steam", "Steam", "2.10.91.91", null, "winget"),
            new InstalledPackage("Discord.Discord", "Discord", "1.0.9170", "1.0.9175", "winget"),
            new InstalledPackage("Mozilla.Firefox", "Mozilla Firefox", "131.0", "132.0.1", "winget"),
            new InstalledPackage("7zip.7zip", "7-Zip", "24.08", null, "winget"),
        ]);
        packages.Community.AddRange(
        [
            new PackageInfo("VideoLAN.VLC", "VLC media player", "3.0.21"),
            new PackageInfo("Daum.PotPlayer", "PotPlayer", "250226"),
            new PackageInfo("MPC-BE.MPC-BE", "MPC-BE player", "1.8.3"),
        ]);
        return packages;
    }

    /// <summary>A Steam game, an Epic one, a GOG one, and VALORANT added by its .exe (see NewEngine).</summary>
    private static FakeGameSources Games() => new FakeGameSources()
        .Key(@"SOFTWARE\WOW6432Node\Valve\Steam", ("InstallPath", @"C:\Program Files (x86)\Steam"))
        .File(@"C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf", """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"C:\\Program Files (x86)\\Steam"
            		"apps" { "730" "1" }
            	}
            }
            """)
        .File(@"C:\Program Files (x86)\Steam\steamapps\appmanifest_730.acf", """
            "AppState"
            {
            	"appid"		"730"
            	"name"		"Counter-Strike 2"
            	"installdir"		"Counter-Strike Global Offensive"
            }
            """)
        .Directory(@"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive")
        .File(@"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\A1.item", """
            { "DisplayName": "Fortnite", "AppName": "Fortnite", "InstallLocation": "C:\\Program Files\\Epic Games\\Fortnite",
              "LaunchExecutable": "FortniteGame/Binaries/Win64/FortniteLauncher.exe", "bIsIncompleteInstall": false }
            """)
        .Directory(@"C:\Program Files\Epic Games\Fortnite")
        .Key(@"SOFTWARE\WOW6432Node\GOG.com\Games\1207658924",
            ("gameName", "The Witcher 3: Wild Hunt"), ("path", @"D:\GOG Games\The Witcher 3 Wild Hunt"),
            ("exe", @"D:\GOG Games\The Witcher 3 Wild Hunt\bin\x64\witcher3.exe"))
        .Directory(@"D:\GOG Games\The Witcher 3 Wild Hunt")
        .File(Valorant);

    /// <summary>Something to find in most cleanup categories, including Windows.old (which can't be undone) and a file in use.</summary>
    private static FakeCleanupSystem Cleanup()
    {
        const long Mb = 1024 * 1024;
        var pc = new FakeCleanupSystem();
        pc.AddFile(@"C:\Windows\Temp\MpCmdRun.log", 48 * Mb);
        pc.AddFile($@"{FakeCleanupSystem.Profile}\AppData\Local\Temp\chrome_installer.log", 312 * Mb);
        pc.AddFile($@"{FakeCleanupSystem.Profile}\AppData\Local\Temp\locked.tmp", 6 * Mb, inUse: true);
        pc.AddFile(@"C:\Windows\SoftwareDistribution\Download\4f2a\windows11.0-kb5044284-x64.cab", 1240 * Mb);
        pc.AddFile($@"{FakeCleanupSystem.Profile}\AppData\Local\NVIDIA\DXCache\a1b2c3.nvph", 640 * Mb);
        pc.AddFile($@"{FakeCleanupSystem.Profile}\AppData\Local\D3DSCache\5e1f\data.bin", 210 * Mb);
        pc.AddFile($@"{FakeCleanupSystem.Profile}\AppData\Local\Microsoft\Windows\Explorer\thumbcache_256.db", 96 * Mb);
        pc.AddFile(@"C:\Windows.old\Windows\System32\ntoskrnl.exe", 18_400 * Mb);
        pc.AddFile($@"C:\$Recycle.Bin\{User}\$R1A2B3C.zip", 730 * Mb);
        return pc;
    }

    /// <summary>Reviewed services, one locked for anti-cheat, one Windows Update core, and a driver.</summary>
    private static FakeServiceManager Services() => new FakeServiceManager()
        .Add("SysMain", ServiceStart.Automatic, displayName: "SysMain")
        .Add("Spooler", ServiceStart.Automatic, displayName: "Print Spooler")
        .Add("WerSvc", ServiceStart.Manual, displayName: "Windows Error Reporting Service")
        .Add("wuauserv", ServiceStart.Manual, displayName: "Windows Update")
        .Add("vgc", ServiceStart.Manual, displayName: "vgc")
        .Add("AdobeARMservice", ServiceStart.Automatic, displayName: "Adobe Acrobat Update Service")
        .Add("RTCore64", ServiceStart.System, ServiceKind.Driver, "RTCore64");

    /// <summary>Run keys, a Startup folder shortcut, a Store app and a scheduled task, with publishers and sign-in costs.</summary>
    private static StartupAdapters Startup()
    {
        const string Nvidia = @"C:\Program Files\NVIDIA Corporation\NVIDIA app\CEF\NVIDIA app.exe";
        const string Updater = @"C:\Program Files (x86)\Google\GoogleUpdater\updater.exe";
        const string Spotify = @"C:\Users\aden\AppData\Roaming\Spotify\Spotify.exe";
        var sources = new FakeStartupSources();
        sources.Run.Add(new RunValue(RunKey.CurrentUser, "Discord", $"\"{Discord}\" --processStart Discord.exe", Discord));
        sources.Run.Add(new RunValue(RunKey.CurrentUser, "Steam", $"\"{Steam}\" -silent", Steam));
        sources.Run.Add(new RunValue(RunKey.LocalMachine, "NvBackend", $"\"{Nvidia}\" --minimized", Nvidia));
        sources.Files.Add(new StartupFile(StartupFolder.User, "Spotify.lnk",
            @"C:\Users\aden\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\Spotify.lnk", Spotify));
        sources.Store.Add(new StoreStartupTask("Microsoft.WindowsTerminal_8wekyb3d8bbwe", "StartTerminalOnLoginTask", "Terminal",
            "Microsoft Corporation", EnabledByDefault: false, @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal\WindowsTerminal.exe"));
        var tasks = new FakeScheduledTasks();
        tasks.Tasks.Add(new ScheduledTaskInfo(@"\GoogleSystem\GoogleUpdater\GoogleUpdaterTaskSystem", true, true,
            "Google LLC", $"\"{Updater}\" --wake --system", Updater));
        var signatures = new FakeFileSignatures();
        signatures.Files[Discord] = new FileSignature(SignatureStatus.Signed, "Discord Inc.");
        signatures.Files[Steam] = new FileSignature(SignatureStatus.Signed, "Valve Corp.");
        signatures.Files[Nvidia] = new FileSignature(SignatureStatus.Signed, "NVIDIA Corporation");
        signatures.Files[Updater] = new FileSignature(SignatureStatus.Signed, "Google LLC");
        var impact = new FakeStartupImpactLog();
        impact.Measurements.Add(new StartupMeasurement(Discord, TimeSpan.FromMilliseconds(1500), 48_000_000));
        impact.Measurements.Add(new StartupMeasurement(Steam, TimeSpan.FromMilliseconds(2400), 96_000_000));
        impact.Measurements.Add(new StartupMeasurement(Nvidia, TimeSpan.FromMilliseconds(380), 12_000_000));
        return new StartupAdapters(sources, tasks, signatures, impact);
    }
}
