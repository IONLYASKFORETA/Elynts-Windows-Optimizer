using Elynt.Core.Journal;
using Elynt.Core.Memory;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>The service's standby-list cleaner, switched on and off by the possible-placebo tweak.</summary>
public class StandbyListCleanerTests
{
    private const long MiB = 1024 * 1024;

    private readonly FakeRegistry _registry = new();
    private readonly FakeMemoryLists _memory = new();

    private StandbyListCleaner Cleaner() => new(_registry, _memory);

    private void TurnOn() =>
        new Engine(Catalogue.Default, new SystemAdapters(_registry, new FakeSystemInfo()), new ChangeJournal(new InMemoryJournalStore()), "S-1-5-21-test")
            .Apply(PlaceboTweaks.StandbyListCleaner.Id);

    [Fact]
    public void Nothing_is_purged_while_the_tweak_is_off()
    {
        _memory.Sizes = new(StandbyBytes: 8000 * MiB, FreeBytes: 100 * MiB);

        Assert.False(Cleaner().Tick());
        Assert.Equal(0, _memory.Purges);
    }

    [Fact]
    public void With_the_tweak_on_a_big_standby_list_is_purged_when_free_memory_runs_low()
    {
        TurnOn();
        _memory.Sizes = new(StandbyBytes: 8000 * MiB, FreeBytes: 100 * MiB);

        Assert.True(Cleaner().Tick());
        Assert.Equal(1, _memory.Purges);
    }

    [Theory]
    [InlineData(8000, 4000)]
    [InlineData(500, 100)]
    public void With_the_tweak_on_nothing_is_purged_while_free_memory_is_plentiful_or_the_list_is_small(long standbyMiB, long freeMiB)
    {
        TurnOn();
        _memory.Sizes = new(StandbyBytes: standbyMiB * MiB, FreeBytes: freeMiB * MiB);

        Assert.False(Cleaner().Tick());
        Assert.Equal(0, _memory.Purges);
    }

    private sealed class FakeMemoryLists : IMemoryLists
    {
        public MemoryListSizes Sizes { get; set; } = new(0, 0);
        public int Purges { get; private set; }

        public MemoryListSizes Read() => Sizes;

        public void PurgeStandby() => Purges++;
    }
}
