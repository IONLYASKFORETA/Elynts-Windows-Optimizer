using Elynt.Core.Cleanup;
using Elynt.Core.Journal;
using Elynt.Core.Repair;
using Elynt.Core.Safety;
using Elynt.Core.Setup;
using Elynt.Core.Tweaks;
using Elynt.Windows;
using Elynt.Service;
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
    builder.Services.AddSingleton(new ChangeJournal(new JsonFileJournalStore(ServicePaths.Journal)));
    builder.Services.AddSingleton(services => new SafetyNet(
        new WindowsRestorePoints(),
        new WindowsStoreBackups(ServicePaths.Backups),
        logger: services.GetRequiredService<ILogger<SafetyNet>>()));
    builder.Services.AddSingleton<IRepairLogStore>(new FileRepairLogStore(ServicePaths.RepairLogs));
    builder.Services.AddSingleton<ICleanupScheduleStore>(new JsonFileCleanupScheduleStore(ServicePaths.CleanupSchedule));
    builder.Services.AddHostedService<EngineHost>();
    builder.Services.AddHostedService<WeeklyCleanupService>();

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
