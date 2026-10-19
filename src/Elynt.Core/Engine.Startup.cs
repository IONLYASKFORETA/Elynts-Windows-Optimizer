using Elynt.Core.Journal;
using Elynt.Core.Startup;

namespace Elynt.Core;

/// <summary>The Startup page's commands. The logic lives in <see cref="StartupManager"/>.</summary>
public sealed partial class Engine
{
    public IReadOnlyList<StartupItemView> ListStartupItems() => Startup.List();

    public StartupCommandResult SetStartupItemEnabled(string itemId, bool enabled) => Startup.SetEnabled(itemId, enabled);

    public StartupCommandResult DeleteStartupItem(string itemId, bool confirmDestructive) => Startup.Delete(itemId, confirmDestructive);

    public IReadOnlyList<StartupItemView> CheckNewStartupItems() => Startup.CheckNew();

    /// <summary>
    /// The name the Undo history shows for a journal entry: a startup switch's item, or the name of the tweak or
    /// service change behind it.
    /// </summary>
    private string NameOf(JournalEntry entry) =>
        StartupManager.Owns(entry.TweakId) ? Startup.NameOf(entry.TweakId)
        : TryResolve(entry.TweakId, entry, out var tweak) ? tweak.Name
        : entry.TweakId;

    private CommandResult UndoStartupChange(JournalEntry entry)
    {
        if (entry.Status is not (EntryStatus.Applying or EntryStatus.Applied))
            return new(false, "This change has already been undone.", null);
        if (!IsUndoable(entry))
            return new(false, "A newer change to this startup item comes after this one. Undo the newer change instead.", null);

        var (succeeded, message) = Startup.Undo(entry);
        return new(succeeded, message, null);
    }
}
