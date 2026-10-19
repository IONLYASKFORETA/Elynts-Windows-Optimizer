using Elynt.Core.Registry;

namespace Elynt.Core.Memory;

/// <summary>How much memory sits in the standby list (cached file data) and how much is free.</summary>
public sealed record MemoryListSizes(long StandbyBytes, long FreeBytes);

/// <summary>Windows' memory lists. Purging needs the service's SYSTEM rights.</summary>
public interface IMemoryLists
{
    MemoryListSizes Read();

    /// <summary>Empties the standby list, as RAMMap's "Empty Standby List" does.</summary>
    void PurgeStandby();
}

/// <summary>
/// What the standby-list cleaner tweak switches on: the service checks every few seconds and empties the standby
/// list when it is big and free memory runs low, with the same defaults as Wagnardsoft's ISLC (both 1024 MB).
/// The tweak only sets the switch, so undo is the journal's usual registry restore.
/// </summary>
public sealed class StandbyListCleaner(IRegistry registry, IMemoryLists memory)
{
    /// <summary>The app's own switch. Present and 1 means on.</summary>
    public static RegistryLocation Switch { get; } =
        new(RegistryRoot.LocalMachine, @"SOFTWARE\Elynt Optimizer\StandbyListCleaner", "Enabled");

    public static TimeSpan Interval { get; } = TimeSpan.FromSeconds(10);

    private const long MinimumStandbyBytes = 1024L * 1024 * 1024;
    private const long LowFreeBytes = 1024L * 1024 * 1024;

    /// <summary>Checks once, purging when it should. Returns whether it purged.</summary>
    public bool Tick()
    {
        if (!RegistryValue.DWord(1).Equals(registry.GetValue(Switch)))
            return false;

        var sizes = memory.Read();
        if (sizes.StandbyBytes < MinimumStandbyBytes || sizes.FreeBytes >= LowFreeBytes)
            return false;

        memory.PurgeStandby();
        return true;
    }
}
