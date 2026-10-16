using Elynt.Core.Journal;
using Elynt.Core.Safety;
using Elynt.Windows;
using Elynt.Core.Tweaks;
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
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddWindowsService(options => options.ServiceName = "ElyntOptimizer");
    builder.Services.AddSerilog();
    builder.Services.AddSingleton(Catalogue.Default);
    builder.Services.AddSingleton(new ChangeJournal(new JsonFileJournalStore(ServicePaths.Journal)));
    builder.Services.AddSingleton(services => new SafetyNet(
        new WindowsRestorePoints(),
        new WindowsStoreBackups(ServicePaths.Backups),
        logger: services.GetRequiredService<ILogger<SafetyNet>>()));
    builder.Services.AddHostedService<EngineHost>();

    builder.Build().Run();
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
