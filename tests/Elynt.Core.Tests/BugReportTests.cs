using System.Text;
using Elynt.Core.Diagnostics;

namespace Elynt.Core.Tests;

public sealed class BugReportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"elynt-logs-{Guid.NewGuid():N}");

    public BugReportTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void The_report_holds_the_newest_log_of_each_kind_even_while_it_is_still_being_written()
    {
        var old = Path.Combine(_folder, "app-20261014.log");
        File.WriteAllText(old, "old day");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));
        var current = Path.Combine(_folder, "app-20261016.log");
        // The app's logger keeps today's file open for writing.
        using var writer = new FileStream(current, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        writer.Write(Encoding.UTF8.GetBytes("App started\nSomething broke\n"));
        writer.Flush();

        var report = BugReport.Build([new("App log", _folder, "app-*.log"), new("Service log", Path.Combine(_folder, "missing"), "service-*.log")]);

        Assert.Contains("== App log: " + current, report);
        Assert.Contains("Something broke", report);
        Assert.DoesNotContain("old day", report);
        Assert.Contains("== Service log: none found", report);
    }

    [Fact]
    public void A_huge_log_is_cut_to_its_most_recent_part()
    {
        var path = Path.Combine(_folder, "app-1.log");
        File.WriteAllText(path, new string('a', 300_000) + "THE END");

        var report = BugReport.Build([new("App log", _folder, "app-*.log")], maxCharsPerLog: 1000);

        Assert.Contains("THE END", report);
        Assert.True(report.Length < 2000);
        Assert.Contains("(earlier lines left out)", report);
    }
}
