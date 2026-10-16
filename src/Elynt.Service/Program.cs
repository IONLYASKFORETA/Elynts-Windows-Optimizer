using Elynt.Core.Journal;
using Elynt.Core.Tuning;
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
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddWindowsService(options => options.ServiceName = "ElyntOptimizer");
    builder.Services.AddSerilog();
    builder.Services.AddSingleton(Catalogue.Default);
    builder.Services.AddSingleton(new ChangeJournal(new JsonFileJournalStore(ServicePaths.Journal)));
    builder.Services.AddSingleton(new TuningSafety(new JsonFileTuningStore(ServicePaths.Tuning)));
    builder.Services.AddSingleton<AdlxGpuTuning>();
    builder.Services.AddSingleton(sp => new GpuTuning(
        [sp.GetRequiredService<AdlxGpuTuning>()],
        sp.GetRequiredService<TuningSafety>(),
        logger: sp.GetRequiredService<ILogger<GpuTuning>>()));
    // Before the pipe: hardware is recovered before any command arrives, and closed after the pipe stops.
    builder.Services.AddHostedService<TuningHost>();
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
