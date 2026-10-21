using Elynt.Core.Cleanup;
using Elynt.Core.Journal;
using Elynt.Core.Profiles;
using Elynt.Core.Repair;
using Elynt.Core.Safety;
using Elynt.Core.Setup;
using Elynt.Core.Tuning;
using Elynt.Core.Tuning.Cpu;
using Elynt.Core.Tweaks;
using Elynt.Service;
using Elynt.Windows;
using Serilog;

ServicePaths.EnsureCreated();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(
        Path.Combine(ServicePaths.Logs, "service-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        fileSizeLimitBytes: 10 * 1024 * 1024,
        rollOnFileSizeLimit: true)
    .CreateLogger();

try
{
    // Never change anything on an OS the tweaks weren't written for.
    var support = OsSupport.Check(WindowsSystemInfo.Describe());
    if (!support.Supported)
    {
        Log.Fatal("Not starting: {Message}", support.Message);
        return 1;
    }

    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddWindowsService(options => options.ServiceName = "ElyntOptimizer");
    builder.Services.AddSerilog();
    builder.Services.AddSingleton(Catalogue.Default);
    // Pipe commands and per-game profiles take turns in this one lane.
    builder.Services.AddSingleton<Elynt.Core.CommandLane>();
    builder.Services.AddSingleton(new ChangeJournal(new JsonFileJournalStore(ServicePaths.Journal)));
    builder.Services.AddSingleton(services => new SafetyNet(
        new WindowsRestorePoints(),
        new WindowsStoreBackups(ServicePaths.Backups),
        logger: services.GetRequiredService<ILogger<SafetyNet>>()));
    builder.Services.AddSingleton<IRepairLogStore>(new FileRepairLogStore(ServicePaths.RepairLogs));
    builder.Services.AddSingleton<ICleanupScheduleStore>(new JsonFileCleanupScheduleStore(ServicePaths.CleanupSchedule));
    builder.Services.AddSingleton<Elynt.Core.Drift.IDriftStore>(new Elynt.Core.Drift.JsonFileDriftStore(ServicePaths.Drift));
    builder.Services.AddSingleton(new TuningSafety(new JsonFileTuningStore(ServicePaths.Tuning)));
    builder.Services.AddSingleton<AdlxGpuTuning>();
    builder.Services.AddSingleton<IgclGpuTuning>();
    builder.Services.AddSingleton<NvmlLibrary>();
    builder.Services.AddSingleton(sp => new GpuTuning(
        [
            sp.GetRequiredService<AdlxGpuTuning>(),
            sp.GetRequiredService<IgclGpuTuning>(),
            new NvidiaGpuTuning(sp.GetRequiredService<NvmlLibrary>()),
        ],
        sp.GetRequiredService<TuningSafety>(),
        logger: sp.GetRequiredService<ILogger<GpuTuning>>()));
    // CPU tuning reaches the CPU only through PawnIO, and only once the user has turned PawnIO on.
    builder.Services.AddSingleton(sp => new CpuTuning(
        new PawnIoCpuTuning(new WindowsPawnIo(new ProcessInstallerRunner(TimeSpan.FromMinutes(5))), Path.Combine(ServicePaths.PawnIo, "modules")),
        sp.GetRequiredService<TuningSafety>(),
        new JsonFileCpuProfileStore(ServicePaths.CpuProfile),
        new CpuStressTest(),
        new WindowsCpuStabilityEvents(),
        logger: sp.GetRequiredService<ILogger<CpuTuning>>()));
    // Before the pipe: hardware is recovered before any command arrives, and closed after the pipe stops.
    builder.Services.AddHostedService<TuningHost>();
    builder.Services.AddSingleton<IProfileStore>(new JsonFileProfileStore(ServicePaths.Profiles));
    builder.Services.AddSingleton<ProfileNotes>();
    builder.Services.AddSingleton<WindowsProcesses>();
    builder.Services.AddSingleton<WindowsTimerResolution>();
    // Before the pipe: temporary changes left behind by a crash or reboot are undone before any command arrives.
    builder.Services.AddSingleton<ProfileHost>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ProfileHost>());
    builder.Services.AddHostedService<EngineHost>();
    builder.Services.AddHostedService<WeeklyCleanupService>();
    builder.Services.AddHostedService<StandbyListCleanerHost>();
    builder.Services.AddHostedService<CpuReapplyHost>();

    builder.Build().Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "The service stopped unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
