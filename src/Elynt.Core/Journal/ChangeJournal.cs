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
[JsonDerivedType(typeof(Profiles.ClosedAppRecord), "closedApp")]
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

    /// <summary>
    /// True when this part affects every account on the PC (a machine-wide setting, a service, a boot setting, a power
    /// plan); false when it is one account's own settings, which undo has to write back to. A method, not a property,
    /// so it is never saved in the journal: it is read from what each part changed, in old entries too.
    /// </summary>
    public abstract bool AffectsEveryAccount();
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

/// <summary>One apply of one tweak, from one account.</summary>
public sealed class JournalEntry
{
    public required Guid Id { get; init; }
    public required string TweakId { get; init; }

    /// <summary>
    /// The SID of the account the change was made from. An account change belongs to this account alone; a
    /// <see cref="WholePc"/> change belongs to the PC.
    /// </summary>
    public required string User { get; init; }

    public required string OsBuild { get; init; }
    public required DateTimeOffset AppliedAt { get; init; }
    public required IReadOnlyList<StepRecord> Steps { get; init; }
    public EntryStatus Status { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>
    /// Made by a per-game profile: undone when the game closes, and at the next service start if it is still in
    /// place after a crash or reboot.
    /// </summary>
    public bool Temporary { get; set; }

    /// <summary>
    /// A whole-PC change: every part of it affects every account (ADR 0007), so it belongs to the PC and is in every
    /// administrator account's Undo history. Read from the saved parts rather than saved itself, so entries written by
    /// earlier versions classify the same way. A temporary change stays with the account whose game profile made it.
    /// </summary>
    [JsonIgnore]
    public bool WholePc => !Temporary && Steps.All(step => step.AffectsEveryAccount());

    /// <summary>Whether this change is in the account's Undo history: the account's own changes and every whole-PC change.</summary>
    public bool SeenBy(string user) => User == user || WholePc;
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
    public JournalEntry Open(string tweakId, string user, string osBuild, DateTimeOffset at, IReadOnlyList<StepRecord> steps, bool temporary = false)
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
            Temporary = temporary,
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

    public void Close(JournalEntry entry, EntryStatus status, DateTimeOffset at) =>
        Change([entry], e =>
        {
            e.Status = status;
            e.ClosedAt = status == EntryStatus.Applied ? null : at;
        });

    /// <summary>
    /// Marks the newest open change to this tweak as reverted and any older open ones in the undoing account's Undo
    /// history (whole-PC changes from other accounts among them) as superseded, so a second undo can never write back
    /// an older, stale snapshot.
    /// </summary>
    public void CloseReverted(JournalEntry newest, string user, DateTimeOffset at)
    {
        lock (_gate)
        {
            var open = _entries.Where(e =>
                e.TweakId == newest.TweakId && e.SeenBy(user) && e.Status is EntryStatus.Applying or EntryStatus.Applied).ToList();
            Change(open, entry =>
            {
                entry.Status = entry == newest ? EntryStatus.Reverted : EntryStatus.Superseded;
                entry.ClosedAt = at;
            });
        }
    }

    /// <summary>
    /// Marks an older change as superseded by a newer change to the same tweak (a re-apply after drift), so undo
    /// returns to the newer change's before-state and never to the older, stale one.
    /// </summary>
    public void Supersede(JournalEntry older, DateTimeOffset at)
    {
        Change([older], e =>
        {
            e.Status = EntryStatus.Superseded;
            e.ClosedAt = at;
        });
    }

    /// <summary>
    /// Changes entries and saves the journal. When the save fails the entries go back to how they were, so memory
    /// never claims what the disk doesn't hold.
    /// </summary>
    private void Change(IReadOnlyList<JournalEntry> entries, Action<JournalEntry> change)
    {
        lock (_gate)
        {
            var before = entries.Select(e => (e.Status, e.ClosedAt, e.Temporary)).ToList();
            foreach (var entry in entries)
                change(entry);
            try
            {
                _store.Save(_entries);
            }
            catch
            {
                for (var i = 0; i < entries.Count; i++)
                    (entries[i].Status, entries[i].ClosedAt, entries[i].Temporary) = before[i];
                throw;
            }
        }
    }

    /// <summary>
    /// A temporary change the user chose to keep: it is no longer undone when the game closes or the service starts.
    /// </summary>
    public void KeepPermanently(JournalEntry entry) => Change([entry], e => e.Temporary = false);

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

    /// <summary>This account's Undo history, oldest first: its own changes and every whole-PC change, from any account.</summary>
    public IReadOnlyList<JournalEntry> EntriesFor(string user)
    {
        lock (_gate)
        {
            return _entries.Where(e => e.SeenBy(user)).ToList();
        }
    }

    /// <summary>This user's temporary changes still in place, oldest first.</summary>
    public IReadOnlyList<JournalEntry> OpenTemporary(string user)
    {
        lock (_gate)
        {
            return _entries.Where(e => e.User == user && e.Temporary && e.Status is EntryStatus.Applying or EntryStatus.Applied).ToList();
        }
    }

    /// <summary>Every user with a temporary change still in place, e.g. after a crash or reboot mid-game.</summary>
    public IReadOnlyList<string> UsersWithTemporaryChanges()
    {
        lock (_gate)
        {
            return _entries.Where(e => e.Temporary && e.Status is EntryStatus.Applying or EntryStatus.Applied).Select(e => e.User).Distinct().ToList();
        }
    }

    /// <summary>
    /// The newest change to this tweak in this account's Undo history that has not been undone: the account's own, or
    /// a whole-PC change from any account.
    /// </summary>
    public JournalEntry? LatestUndoable(string tweakId, string user)
    {
        lock (_gate)
        {
            return _entries.LastOrDefault(e =>
                e.TweakId == tweakId && e.SeenBy(user) && e.Status is EntryStatus.Applying or EntryStatus.Applied);
        }
    }
}
