using Elynt.Core.Apps;
using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Repair;
using Elynt.Core.Search;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>The sidebar's app-wide search: one Engine query over tweaks, repair tools and catalogue apps.</summary>
public class SearchTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    [Fact]
    public void Finds_a_tweak_by_name_and_names_the_page_it_is_on()
    {
        var engine = EngineFor(Tweak("test.pointer", "Enhance pointer precision off", Sections.Gaming));

        var result = Assert.Single(engine.Search("pointer"));

        Assert.Equal(new SearchResult(SearchResultKind.Tweak, "test.pointer", "Enhance pointer precision off", Sections.Gaming, "A test summary."), result);
    }

    [Fact]
    public void A_name_match_ranks_above_a_match_in_the_summary()
    {
        var engine = EngineFor(
            Tweak("test.background", "Background apps off", Sections.DebloatAndPrivacy, "Stops Store apps running while you game."),
            Tweak("test.game-mode", "Game Mode on", Sections.Gaming, "Windows puts the focused app first."));

        var results = engine.Search("game");

        Assert.Equal(["test.game-mode", "test.background"], results.Select(r => r.Id));
    }

    [Fact]
    public void A_name_starting_with_the_query_ranks_first_then_a_word_starting_with_it_then_the_query_inside_a_word()
    {
        var engine = EngineFor(
            Tweak("test.ethernet", "Ethernet power saving off", Sections.Gaming),
            Tweak("test.nagle", "Nagle off on the network adapter", Sections.Gaming),
            Tweak("test.throttling", "Network throttling off", Sections.Gaming));

        var results = engine.Search("net");

        Assert.Equal(["test.throttling", "test.nagle", "test.ethernet"], results.Select(r => r.Id));
    }

    [Fact]
    public void Ignores_case_and_the_spaces_around_the_query()
    {
        var engine = EngineFor(Tweak("test.game-mode", "Game Mode on", Sections.Gaming));

        Assert.Equal("test.game-mode", Assert.Single(engine.Search("  GAME mode ")).Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_query_finds_nothing(string query)
    {
        var engine = EngineFor(Tweak("test.game-mode", "Game Mode on", Sections.Gaming));

        Assert.Empty(engine.Search(query));
    }

    [Fact]
    public void Finds_repair_tools_and_catalogue_apps_too_each_on_its_own_page()
    {
        var engine = new Engine(
            new Catalogue([Tweak("test.dns", "DNS cache size", Sections.Gaming)]),
            new SystemAdapters(_registry, _system, Repair: new FakeRepairSystem()),
            _journal,
            "S-1-5-21-test",
            apps: new AppCatalogue([new CatalogueApp("Some.DnsJumper", "DNS Jumper", AppGroups.Utilities, "Switches DNS servers.")]),
            repairs: new RepairCatalogue([RepairTool("repair.test-dns", "Reset the DNS cache", "Flushes the DNS cache.")]));

        var results = engine.Search("dns");

        Assert.Equal(
            [
                new SearchResult(SearchResultKind.Tweak, "test.dns", "DNS cache size", Sections.Gaming, "A test summary."),
                new SearchResult(SearchResultKind.App, "Some.DnsJumper", "DNS Jumper", Sections.Apps, "Switches DNS servers."),
                new SearchResult(SearchResultKind.RepairTool, "repair.test-dns", "Reset the DNS cache", Sections.Repair, "Flushes the DNS cache."),
            ],
            results);
    }

    [Fact]
    public void Leaves_out_a_tweak_this_PC_hides_but_finds_one_it_only_greys_out()
    {
        var hidden = Tweak("test.hidden", "Pointer trails off", Sections.Gaming) with
        {
            Applicability = new Applicability(MinBuild: 99999, Hide: true),
        };
        var greyed = Tweak("test.greyed", "Pointer shadow off", Sections.Gaming) with
        {
            Applicability = new Applicability(MinBuild: 99999),
        };
        var engine = EngineFor(hidden, greyed);

        Assert.Equal(["test.greyed"], engine.Search("pointer").Select(r => r.Id));
    }

    [Fact]
    public void Finds_a_hidden_tweak_while_it_still_has_a_change_to_undo()
    {
        var hidden = Tweak("test.hidden", "Pointer trails off", Sections.Gaming) with
        {
            Applicability = new Applicability(Editions: [WindowsEdition.Pro], Hide: true),
        };
        var engine = EngineFor(hidden);
        engine.Apply(hidden.Id);
        _system.Edition = WindowsEdition.Home;

        Assert.Equal(["test.hidden"], engine.Search("pointer").Select(r => r.Id));
    }

    [Fact]
    public void Leaves_out_follow_up_repair_tools_which_only_a_repair_result_offers()
    {
        var engine = new Engine(
            new Catalogue([]),
            new SystemAdapters(_registry, _system, Repair: new FakeRepairSystem()),
            _journal,
            "S-1-5-21-test",
            apps: new AppCatalogue([]),
            repairs: new RepairCatalogue(
            [
                RepairTool("repair.test-check", "Check the disk", "Looks for disk errors."),
                RepairTool("repair.test-fix", "Fix the disk at the next restart", "Fixes the errors a check found.", isFollowUp: true),
            ]));

        Assert.Equal(["repair.test-check"], engine.Search("disk").Select(r => r.Id));
    }

    // ---- helpers ----

    private static RepairToolDefinition RepairTool(string id, string name, string whatItDoes, bool isFollowUp = false) =>
        new(id, name, whatItDoes, RestartNeed.None, "No restart needed.", [], IsFollowUp: isFollowUp);

    /// <summary>A tweak with a registry value of its own, so each one is on or off by itself.</summary>
    private static TweakDefinition Tweak(string id, string name, string section, string summary = "A test summary.") =>
        LabelTests.TestTweak(id, new RegistryValueStep(new RegistryLocation(RegistryRoot.CurrentUser, @"Software\ElyntTest", id), RegistryValue.DWord(1)))
            with { Name = name, Section = section, Summary = summary };

    /// <summary>An Engine with only these tweaks: no repair tools (no repair system) and no catalogue apps.</summary>
    private Engine EngineFor(params TweakDefinition[] tweaks) =>
        new(new Catalogue(tweaks), new SystemAdapters(_registry, _system), _journal, "S-1-5-21-test", apps: new AppCatalogue([]));
}
