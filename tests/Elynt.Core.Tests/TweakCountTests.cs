using Elynt.Core.Journal;
using Elynt.Core.Registry;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>A tweak section's "5 of 20 on" count, over the tweaks the Engine lists.</summary>
public class TweakCountTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    [Fact]
    public void Counts_the_tweaks_that_are_on_out_of_those_that_fit_this_PC()
    {
        _system.OsBuild = "22631.4000";
        var on = Tweak("test.on");
        var off = Tweak("test.off");
        var newerBuild = Tweak("test.newer-build") with { Applicability = new Applicability(MinBuild: 26100) };
        // Already set on this PC, but it doesn't fit it, so it counts neither as on nor towards the total.
        _registry.SetValue(ValueOf(newerBuild.Id), RegistryValue.DWord(1));
        var engine = EngineFor(on, off, newerBuild);
        Assert.True(engine.Apply(on.Id).Succeeded);

        var count = TweakCount.Of(engine.ListTweaks());

        Assert.Equal("1 of 2 on", count.Text);
    }

    [Fact]
    public void A_per_game_choice_has_no_switch_of_its_own_so_it_is_not_counted()
    {
        var on = Tweak("test.on");
        var perGame = new PerGameTweak(Tweak("test.per-game"), exe => [new RegistryValueStep(ValueOf(exe), RegistryValue.DWord(1))]);
        var engine = new Engine(new Catalogue([on], [perGame]), new SystemAdapters(_registry, _system), _journal, "S-1-5-21-test");
        Assert.True(engine.Apply(on.Id).Succeeded);

        var count = TweakCount.Of(engine.ListTweaks());

        Assert.Equal("1 of 1 on", count.Text);
    }

    private static TweakDefinition Tweak(string id) =>
        LabelTests.TestTweak(id, new RegistryValueStep(ValueOf(id), RegistryValue.DWord(1)));

    private static RegistryLocation ValueOf(string id) => new(RegistryRoot.CurrentUser, @"Software\ElyntTest", id);

    private Engine EngineFor(params TweakDefinition[] tweaks) =>
        new(new Catalogue(tweaks), new SystemAdapters(_registry, _system), _journal, "S-1-5-21-test");
}
