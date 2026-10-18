using System.Text.Json.Serialization;
using Elynt.Core.Registry;

namespace Elynt.Core.Journal;

/// <summary>The exact prior state of one step, captured before the change, that knows how to put it back.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RegistryValueRecord), "registryValue")]
[JsonDerivedType(typeof(Services.ServiceConfigRecord), "serviceConfig")]
[JsonDerivedType(typeof(Startup.ScheduledTaskRecord), "scheduledTask")]
[JsonDerivedType(typeof(RegistryListItemRecord), "registryListItem")]
[JsonDerivedType(typeof(RegistryEachKeyRecord), "registryEachKey")]
[JsonDerivedType(typeof(Boot.BootSettingRecord), "bootSetting")]
[JsonDerivedType(typeof(Power.PowerPlanRecord), "powerPlan")]
[JsonDerivedType(typeof(Tweaks.AppRemovalRecord), "appRemoval")]
[JsonDerivedType(typeof(Tweaks.AppDeprovisionRecord), "appDeprovision")]
[JsonDerivedType(typeof(Tweaks.OptionalFeatureRecord), "optionalFeature")]
[JsonDerivedType(typeof(Power.PowerSettingRecord), "powerSetting")]
[JsonDerivedType(typeof(Devices.DeviceEnabledRecord), "deviceEnabled")]
[JsonDerivedType(typeof(Security.ScanExclusionRecord), "scanExclusion")]
public abstract record StepRecord
{
    /// <summary>
    /// Says what something else changed since the app made this change, or null when the target still holds
    /// what the app wrote (or was never changed, if the change was interrupted). Undo refuses to overwrite it.
    /// </summary>
    public abstract string? FindConflict(SystemAdapters system);

    public abstract void Revert(SystemAdapters system);
}

public enum EntryStatus
{
    /// <summary>Prior state saved, change in progress. Left behind if the service died mid-change; still undoable.</summary>
    Applying,
    Applied,
    /// <summary>The change failed part-way and was put back straight away.</summary>
    RolledBack,
    Reverted,
    /// <summary>An older change to a tweak that was undone later: undoing the newest change ends the older ones too.</summary>
    Superseded,
}

/// <summary>One apply of one tweak for one user.</summary>
public sealed class JournalEntry
{
    public required Guid Id { get; init; }
    public required string TweakId { get; init; }

    /// <summary>The SID of the user whose settings were changed.</summary>
    public required string User { get; init; }

    public required string OsBuild { get; init; }
    public required DateTimeOffset AppliedAt { get; init; }
    public required IReadOnlyList<StepRecord> Steps { get; init; }
    public EntryStatus Status { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

/// <summary>
/// The write-ahead change journal. Every entry is saved with the exact prior state before the change it
/// describes is made, so undo works even after a crash.
/// </summary>
public sealed class ChangeJournal
{
    private readonly IJournalStore _store;
    private readonly List<JournalEntry> _entries;
    private readonly Lock _gate = new();

    public ChangeJournal(IJournalStore store)
    {
        _store = store;
        _entries = store.Load().ToList();
    }

    /// <summary>Saves a new entry in the <see cref="EntryStatus.Applying"/> state. Call before changing anything.</summary>
    public JournalEntry Open(string tweakId, string user, string osBuild, DateTimeOffset at, IReadOnlyList<StepRecord> steps)
    {
        var entry = new JournalEntry
        {
            Id = Guid.NewGuid(),
            TweakId = tweakId,
            User = user,
            OsBuild = osBuild,
            AppliedAt = at,
            Steps = steps,
            Status = EntryStatus.Applying,
        };
        lock (_gate)
        {
            _entries.Add(entry);
            try
            {
                _store.Save(_entries);
            }
            catch
            {
                _entries.Remove(entry);
                throw;
            }
        }
        return entry;
    }

    public void Close(JournalEntry entry, EntryStatus status, DateTimeOffset at)
    {
        lock (_gate)
        {
            entry.Status = status;
            entry.ClosedAt = status == EntryStatus.Applied ? null : at;
            _store.Save(_entries);
        }
    }

    /// <summary>
    /// Marks the newest open change to this tweak as reverted and any older open ones as superseded, so a second
    /// undo can never write back an older, stale snapshot.
    /// </summary>
    public void CloseReverted(JournalEntry newest, DateTimeOffset at)
    {
        lock (_gate)
        {
            foreach (var entry in _entries.Where(e =>
                e.TweakId == newest.TweakId && e.User == newest.User && e.Status is EntryStatus.Applying or EntryStatus.Applied))
            {
                entry.Status = entry == newest ? EntryStatus.Reverted : EntryStatus.Superseded;
                entry.ClosedAt = at;
            }
            _store.Save(_entries);
        }
    }

    /// <summary>When this tweak was last applied or undone for this user, or null if it never was.</summary>
    public DateTimeOffset? LastChangedAt(string tweakId, string user)
    {
        lock (_gate)
        {
            return _entries
                .Where(e => e.TweakId == tweakId && e.User == user)
                .Select(e => e.Status switch
                {
                    EntryStatus.Applying or EntryStatus.Applied => e.AppliedAt,
                    EntryStatus.Reverted => e.ClosedAt,
                    _ => null,
                })
                .Max();
        }
    }

    /// <summary>Every tweak this user has a change to undo for, oldest first.</summary>
    public IReadOnlyList<string> UndoableTweakIds(string user)
    {
        lock (_gate)
        {
            return _entries
                .Where(e => e.User == user && e.Status is EntryStatus.Applying or EntryStatus.Applied)
                .Select(e => e.TweakId)
                .Distinct()
                .ToList();
        }
    }

    /// <summary>Every change made for this user, oldest first.</summary>
    public IReadOnlyList<JournalEntry> EntriesFor(string user)
    {
        lock (_gate)
        {
            return _entries.Where(e => e.User == user).ToList();
        }
    }

    /// <summary>The newest change to this tweak for this user that has not been undone.</summary>
    public JournalEntry? LatestUndoable(string tweakId, string user)
    {
        lock (_gate)
        {
            return _entries.LastOrDefault(e =>
                e.TweakId == tweakId && e.User == user && e.Status is EntryStatus.Applying or EntryStatus.Applied);
        }
    }
}
