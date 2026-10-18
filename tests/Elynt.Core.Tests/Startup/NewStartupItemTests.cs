using Elynt.Core.Journal;
using Elynt.Core.Startup;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests.Startup;

/// <summary>The tray note when an app adds itself to startup.</summary>
public class NewStartupItemTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeStartupSources _sources = new();
    private readonly FakeScheduledTasks _tasks = new();
    private readonly InMemoryKnownStartupItems _known = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    private Engine NewEngine(string user = "S-1-5-21-test") => new(
        new Catalogue([]),
        new SystemAdapters(_registry, new FakeSystemInfo())
        {
            Startup = new StartupAdapters(_sources, _tasks, new FakeFileSignatures(), new FakeStartupImpactLog(), _known),
        },
        _journal,
        user);

    private void AddRun(string name) =>
        _sources.Run.Add(new RunValue(RunKey.CurrentUser, name, $@"C:\Apps\{name}.exe", $@"C:\Apps\{name}.exe"));

    [Fact]
    public void The_first_look_takes_note_of_what_is_there_without_alerting()
    {
        AddRun("Discord");

        Assert.Empty(NewEngine().CheckNewStartupItems());
    }

    [Fact]
    public void An_item_added_since_the_last_look_is_reported_once()
    {
        AddRun("Discord");
        var engine = NewEngine();
        engine.CheckNewStartupItems();
        AddRun("Spotify");
        _tasks.Tasks.Add(new ScheduledTaskInfo(@"\Vendor\Launch at sign-in", true, RunsAtSignIn: true, null, null, null));

        var added = engine.CheckNewStartupItems();

        Assert.Equal(["Spotify", "Launch at sign-in"], added.Select(i => i.Name));
        Assert.Empty(engine.CheckNewStartupItems());
    }

    [Fact]
    public void What_was_seen_is_remembered_across_service_restarts()
    {
        AddRun("Discord");
        NewEngine().CheckNewStartupItems();
        AddRun("Spotify");

        Assert.Equal("Spotify", Assert.Single(NewEngine().CheckNewStartupItems()).Name);
    }

    [Fact]
    public void Items_that_are_already_off_and_tasks_that_do_not_run_at_sign_in_are_not_reported()
    {
        NewEngine().CheckNewStartupItems();
        _tasks.Tasks.Add(new ScheduledTaskInfo(@"\Vendor\Updater", true, RunsAtSignIn: false, null, null, null));
        _tasks.Tasks.Add(new ScheduledTaskInfo(@"\Vendor\Off at sign-in", false, RunsAtSignIn: true, null, null, null));

        Assert.Empty(NewEngine().CheckNewStartupItems());
    }

    [Fact]
    public void Each_user_has_their_own_baseline()
    {
        NewEngine("S-1-5-21-aden").CheckNewStartupItems();
        AddRun("Discord");

        Assert.Empty(NewEngine("S-1-5-21-friend").CheckNewStartupItems());
        Assert.Single(NewEngine("S-1-5-21-aden").CheckNewStartupItems());
    }

    [Fact]
    public void The_note_can_turn_the_new_item_off()
    {
        var engine = NewEngine();
        engine.CheckNewStartupItems();
        AddRun("Spotify");
        var added = Assert.Single(engine.CheckNewStartupItems());

        Assert.True(engine.SetStartupItemEnabled(added.Id, enabled: false).Succeeded);
        Assert.False(engine.ListStartupItems().Single().Enabled);
    }
}
