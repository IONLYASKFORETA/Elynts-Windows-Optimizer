using Elynt.Core.Journal;
using Elynt.Core.Startup;

namespace Elynt.Core;

/// <summary>The Startup page's commands. The logic lives in <see cref="StartupManager"/>.</summary>
public sealed partial class Engine
{
    public IReadOnlyList<StartupItemView> ListStartupItems() => _startup.List();

    public StartupCommandResult SetStartupItemEnabled(string itemId, bool enabled) => _startup.SetEnabled(itemId, enabled);

    /// <summary>The name the Undo history shows for a journal entry: a tweak's name, or a startup switch's item.</summary>
    private string NameOf(string journalId) =>
        StartupManager.Owns(journalId) ? _startup.NameOf(journalId)
        : catalogue.TryGet(journalId, out var tweak) ? tweak.Name
        : journalId;

    private CommandResult UndoStartupChange(JournalEntry entry)
    {
        if (entry.Status is not (EntryStatus.Applying or EntryStatus.Applied))
            return new(false, "This change has already been undone.", null);
        if (!IsUndoable(entry))
            return new(false, "A newer change to this startup item comes after this one. Undo the newer change instead.", null);

        var (succeeded, message) = _startup.Undo(entry);
        return new(succeeded, message, null);
    }
}
