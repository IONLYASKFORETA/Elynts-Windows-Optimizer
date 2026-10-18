using Elynt.Core.Startup;

namespace Elynt.Core.Tests.Startup;

/// <summary>What the real adapters read from Windows: task definitions and startup command lines.</summary>
public class StartupParsingTests
{
    [Theory]
    [InlineData(@"""C:\Program Files\Discord\Update.exe"" --processStart Discord.exe", @"C:\Program Files\Discord\Update.exe")]
    [InlineData(@"C:\Program Files\Steam\steam.exe -silent", @"C:\Program Files\Steam\steam.exe")]
    [InlineData(@"C:\Tools\tool.EXE", @"C:\Tools\tool.EXE")]
    [InlineData(@"""C:\Apps\launcher.cmd""", @"C:\Apps\launcher.cmd")]
    [InlineData(@"C:\Apps\run.bat /min", @"C:\Apps\run.bat")]
    [InlineData("", null)]
    public void The_program_a_command_line_starts_is_found(string command, string? expected)
    {
        Assert.Equal(expected, StartupCommandLine.ExecutablePath(command));
    }

    private const string LogonTask = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Author>Logitech</Author>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
            </LogonTrigger>
          </Triggers>
          <Settings>
            <Enabled>false</Enabled>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>"C:\Program Files\LGHUB\lghub.exe"</Command>
              <Arguments>--background</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    [Fact]
    public void A_task_definition_gives_its_author_trigger_state_and_program()
    {
        var task = ScheduledTaskXml.Parse(@"\Logitech\LGHUB Launch", LogonTask);

        Assert.Equal(new ScheduledTaskInfo(
            @"\Logitech\LGHUB Launch", Enabled: false, RunsAtSignIn: true, "Logitech",
            @"""C:\Program Files\LGHUB\lghub.exe"" --background", @"C:\Program Files\LGHUB\lghub.exe"), task);
    }

    [Fact]
    public void A_task_with_no_enabled_setting_is_on_and_one_with_only_a_timer_is_not_a_sign_in_task()
    {
        var xml = LogonTask
            .Replace("<Enabled>false</Enabled>", "")
            .Replace("<LogonTrigger>", "<TimeTrigger>").Replace("</LogonTrigger>", "</TimeTrigger>");

        var task = ScheduledTaskXml.Parse(@"\Vendor\Task", xml)!;

        Assert.True(task.Enabled);
        Assert.False(task.RunsAtSignIn);
    }

    [Fact]
    public void A_damaged_task_file_is_skipped()
    {
        Assert.Null(ScheduledTaskXml.Parse(@"\Vendor\Task", "<Task"));
    }
}
