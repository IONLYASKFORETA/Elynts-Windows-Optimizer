using Elynt.Core.Startup;

namespace Elynt.Core.Tests.Startup;

/// <summary>Task Manager's impact column, from Windows' own measurements at the last sign-in.</summary>
public class StartupImpactTests
{
    private const string Exe = @"C:\Apps\app.exe";

    [Theory]
    [InlineData(1001, 0, StartupImpact.High)]
    [InlineData(0, 3 * 1024 * 1024 + 1, StartupImpact.High)]
    [InlineData(1000, 3 * 1024 * 1024, StartupImpact.Medium)]
    [InlineData(300, 0, StartupImpact.Medium)]
    [InlineData(0, 300 * 1024, StartupImpact.Medium)]
    [InlineData(299, 300 * 1024 - 1, StartupImpact.Low)]
    public void Impact_follows_task_managers_thresholds(int cpuMs, long diskBytes, StartupImpact expected)
    {
        var measurements = new[] { new StartupMeasurement(Exe, TimeSpan.FromMilliseconds(cpuMs), diskBytes) };

        Assert.Equal(expected, StartupImpactRules.Classify(@"c:\apps\APP.exe", measurements));
    }

    [Fact]
    public void A_program_with_no_measurement_is_not_measured()
    {
        Assert.Equal(StartupImpact.NotMeasured, StartupImpactRules.Classify(Exe, []));
        Assert.Equal(StartupImpact.NotMeasured, StartupImpactRules.Classify(null, []));
    }

    [Fact]
    public void Windows_startup_info_file_is_read_into_measurements()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-16"?>
            <StartupData>
              <Process Name="C:\Apps\app.exe" PID="4242" StartTime="133730000000000000">
                <StartTime>133730000000000000</StartTime>
                <CpuUsage>1250</CpuUsage>
                <DiskUsage>524288</DiskUsage>
                <ParentPID>812</ParentPID>
                <ParentName>explorer.exe</ParentName>
              </Process>
              <Process Name="C:\Apps\broken.exe" PID="1">
                <CpuUsage>not a number</CpuUsage>
              </Process>
            </StartupData>
            """;

        var measurement = Assert.Single(StartupInfoFile.Parse(xml));

        Assert.Equal(new StartupMeasurement(Exe, TimeSpan.FromMilliseconds(1250), 524288), measurement);
    }

    [Fact]
    public void A_damaged_startup_info_file_gives_no_measurements()
    {
        Assert.Empty(StartupInfoFile.Parse("<StartupData><Process"));
    }
}
