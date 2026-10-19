using Elynt.Core;
using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Startup;
using Elynt.Core.Tweaks;

namespace Elynt.Windows.Tests;

/// <summary>Turning a real Run key item off and on through Task Manager's StartupApproved switch.</summary>
public sealed class RealStartupTests : IDisposable
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private readonly string _name = $"ElyntOptimizerTest-{Guid.NewGuid():N}";

    private static readonly string CurrentUserSid =
        OperatingSystem.IsWindows() ? System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value : "";

    private readonly WindowsRegistry _registry = new(CurrentUserSid);

    public RealStartupTests() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs the real Windows registry.");

    [Fact]
    public void A_run_item_is_listed_switched_off_like_task_manager_and_put_back_exactly()
    {
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        _registry.SetValue(new RegistryLocation(RegistryRoot.CurrentUser, RunPath, _name), RegistryValue.String($"\"{cmd}\" /c exit"));
        var approved = new RegistryLocation(RegistryRoot.CurrentUser, ApprovedPath, _name);
        var before = _registry.GetValue(approved);
        var engine = NewEngine();

        var item = engine.ListStartupItems().Single(i => i.Name == _name);
        Assert.Equal(StartupSource.RunKey, item.Source);
        Assert.Equal(cmd, item.Command.Split('"')[1], ignoreCase: true);
        Assert.True(item.Enabled);
        Assert.NotNull(item.Publisher);

        Assert.True(engine.SetStartupItemEnabled(item.Id, false).Succeeded);
        Assert.Equal(0x03, _registry.GetValue(approved)!.Data[0]);
        Assert.False(engine.ListStartupItems().Single(i => i.Id == item.Id).Enabled);

        Assert.True(engine.SetStartupItemEnabled(item.Id, true).Succeeded);
        Assert.Equal(before, _registry.GetValue(approved));
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
            return;
        _registry.DeleteValue(new RegistryLocation(RegistryRoot.CurrentUser, RunPath, _name));
        _registry.DeleteValue(new RegistryLocation(RegistryRoot.CurrentUser, ApprovedPath, _name));
    }

    private Engine NewEngine() => new(
        new Catalogue([]),
        new SystemAdapters(_registry, new WindowsSystemInfo())
        {
            Startup = new StartupAdapters(
                new WindowsStartupSources(CurrentUserSid), new WindowsScheduledTasks(), new WindowsFileSignatures(),
                new WindowsStartupImpactLog(CurrentUserSid)),
        },
        new ChangeJournal(new InMemoryJournalStore()),
        CurrentUserSid);
}
