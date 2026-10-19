using Elynt.Core.Journal;
using Elynt.Core.Safety;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Logging;

namespace Elynt.Core.Startup;

/// <summary>
/// The Startup page's commands for one user: every startup item with publisher, signature and impact; turning one
/// off or on through the same switch Task Manager uses (journaled, so it shows in Undo history); deleting one
/// (destructive); and spotting items added since the last look.
/// </summary>
/// <param name="apply">
/// The Engine's own apply: hard rules, safety net, write-ahead journal and roll-back, for a switch described as a
/// one-step tweak.
/// </param>
internal sealed class StartupManager(
    SystemAdapters system,
    ChangeJournal journal,
    string user,
    SafetyNet? safetyNet,
    TimeProvider clock,
    ILogger _log,
    Func<TweakDefinition, ChangeWording, CommandResult> apply)
{
    /// <summary>Journal entries for startup switches use this prefix before the item id instead of a tweak id.</summary>
    public const string JournalPrefix = "startup:";

    public static bool Owns(string journalId) => journalId.StartsWith(JournalPrefix, StringComparison.Ordinal);

    public IReadOnlyList<StartupItemView> List()
    {
        if (system.Startup is not { } startup)
            return [];

        var impact = startup.Impact.Read();
        return Read(startup).Select(item => Describe(item, impact)).ToList();
    }

    public StartupCommandResult SetEnabled(string itemId, bool enabled)
    {
        if (Find(itemId) is not { } item)
            return Unknown(itemId);
        if (!item.Switch.CanSwitch(system))
            return Failed(item, "A policy controls this item, so it can't be switched here. Nothing was changed.");

        try
        {
            if (item.Switch.IsEnabled(system) == enabled)
                return Done(item, enabled ? "Already on. Nothing was changed." : "Already off. Nothing was changed.");

            // Every flip is journaled, so flipping back is undoing the last flip: that puts back exactly what was
            // there, including a value Task Manager wrote. If something else changed it since, take a fresh change.
            if (journal.LatestUndoable(JournalId(item), user) is { } last && Conflicts(last).Count == 0)
            {
                Revert(last);
                return Done(item, enabled ? "Turned on. It starts at sign-in again." : "Turned off. The previous setting is back.");
            }

            return Change(item, item.Switch.StepFor(enabled, clock.GetUtcNow()), enabled
                ? "Turned on. It starts at sign-in again."
                : "Turned off. It won't start at sign-in; switch it back on any time.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Switching startup item {Item} failed", item.Id);
            return Failed(item, $"Couldn't change this: {ex.Message}");
        }
    }

    public StartupCommandResult Delete(string itemId, bool confirmDestructive)
    {
        if (Find(itemId) is not { } item)
            return Unknown(itemId);
        if (item.Remove is null)
            return Failed(item, item.Source == StartupSource.StoreApp
                ? "A Store app's startup task can only be turned off here; uninstall the app to remove it. Nothing was changed."
                : "This item can't be deleted here. Nothing was changed.");
        if (item.DeleteTarget is { } target && HardRules.Violation(target) is { } violation)
            return Failed(item, $"This change is never allowed: {violation}. Nothing was changed.");
        if (!confirmDestructive)
            return Failed(item, $"Deleting {item.Name} can't be undone, so it needs a separate confirmation first. " +
                                "Turning it off instead stops it starting and can be undone. Nothing was changed.");

        try
        {
            // A task deletion is covered by the whole-store backup of scheduled tasks.
            if (item.Source is StartupSource.SignInTask or StartupSource.ScheduledTask)
                safetyNet?.BeforeChange(AsTweak(item, new ScheduledTaskStep(item.Location, false)));
            item.Remove(system);
            _log.LogInformation("Deleted startup item {Item} for {User}", item.Id, user);
            return new StartupCommandResult(true, $"Deleted {item.Name}. It won't start at sign-in, and this can't be undone.", null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Deleting startup item {Item} failed", item.Id);
            return Failed(item, $"Couldn't delete this: {ex.Message}");
        }
    }

    public IReadOnlyList<StartupItemView> CheckNew()
    {
        if (system.Startup is not { } startup)
            return [];

        var items = Read(startup).ToList();
        var known = startup.Known.Load(user);
        var seen = new HashSet<string>(known ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
        seen.UnionWith(items.Select(i => i.Id));
        startup.Known.Save(user, seen);
        if (known is null)
            return [];

        // Only things that will actually start at sign-in are worth a note.
        var impact = startup.Impact.Read();
        var added = items
            .Where(i => !known.Contains(i.Id) && i.Source != StartupSource.ScheduledTask && i.Switch.IsEnabled(system))
            .Select(i => Describe(i, impact))
            .ToList();
        if (added.Count > 0)
            _log.LogInformation("New startup items for {User}: {Items}", user, added.Select(i => i.Id));
        return added;
    }

    /// <summary>Undoes one journaled startup switch from the Undo history.</summary>
    public (bool Succeeded, string Message) Undo(JournalEntry entry)
    {
        try
        {
            var conflicts = Conflicts(entry);
            if (conflicts.Count > 0)
                return (false, $"Didn't undo this. {string.Join(" ", conflicts)} Undoing now would overwrite that, so nothing was changed.");
            Revert(entry);
            return (true, "Undone. The previous setting is back.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Undoing startup change {Entry} failed", entry.Id);
            return (false, $"Couldn't undo this: {ex.Message}");
        }
    }

    /// <summary>The name the Undo history shows for a startup switch.</summary>
    public string NameOf(string journalId)
    {
        var itemId = journalId[JournalPrefix.Length..];
        var name = Find(itemId)?.Name ?? StartupItems.NameFromId(itemId);
        return $"Startup item: {name}";
    }

    /// <summary>Makes the switch through the Engine's apply, worded for the Startup page.</summary>
    private StartupCommandResult Change(StartupItem item, TweakStep step, string message)
    {
        var result = apply(AsTweak(item, step), ChangeWording.Tweak with
        {
            Done = message,
            AlreadyDone = "Already set. Nothing was changed.",
            CouldNot = "Couldn't change this",
            UndoHint = "undo it from Undo history",
        });
        if (result.Succeeded)
            _log.LogInformation("Startup item {Item} switched for {User}", item.Id, user);
        return result.Succeeded ? Done(item, result.Message) : Failed(item, result.Message);
    }

    private List<string> Conflicts(JournalEntry entry) =>
        entry.Steps.Select(step => step.FindConflict(system)).OfType<string>().ToList();

    private void Revert(JournalEntry entry)
    {
        for (var i = entry.Steps.Count - 1; i >= 0; i--)
            entry.Steps[i].Revert(system);
        journal.CloseReverted(entry, clock.GetUtcNow());
    }

    /// <summary>The safety net works on tweaks, so a switch is described as a one-step tweak.</summary>
    private static TweakDefinition AsTweak(StartupItem item, TweakStep step) => new(
        JournalId(item), Sections.StartupAndServices, $"switching startup item {item.Name}", "", "", [], EvidenceTier.Proven, "", [step]);

    private static string JournalId(StartupItem item) => JournalPrefix + item.Id;

    private StartupItem? Find(string itemId) =>
        system.Startup is { } startup ? Read(startup).FirstOrDefault(i => i.Id.Equals(itemId, StringComparison.OrdinalIgnoreCase)) : null;

    /// <summary>Every item in page order: Run keys, Startup folders, sign-in tasks, Store apps, then other tasks.</summary>
    private static IEnumerable<StartupItem> Read(StartupAdapters startup)
    {
        var tasks = startup.Tasks.List()
            .Where(t => !StartupItems.IsMicrosoftTask(t.Path))
            .Select(StartupItems.FromTask)
            .ToList();
        return startup.Sources.RunValues().Select(StartupItems.FromRunValue)
            .Concat(startup.Sources.StartupFiles().Select(StartupItems.FromStartupFile))
            .Concat(tasks.Where(t => t.Source == StartupSource.SignInTask))
            .Concat(startup.Sources.StoreStartupTasks().Select(StartupItems.FromStoreTask))
            .Concat(tasks.Where(t => t.Source == StartupSource.ScheduledTask));
    }

    private StartupItemView Describe(StartupItem item, IReadOnlyList<StartupMeasurement> measurements)
    {
        var signature = item.ExecutablePath is { } path
            ? system.Startup!.Signatures.Inspect(path)
            : new FileSignature(SignatureStatus.Unknown, null);
        return new StartupItemView(
            item.Id,
            item.Name,
            item.Source,
            item.Location,
            item.Command,
            item.Publisher ?? signature.Publisher ?? item.FallbackPublisher,
            item.Signature ?? signature.Status,
            StartupImpactRules.Classify(item.ExecutablePath, measurements),
            item.Switch.IsEnabled(system),
            item.Switch.CanSwitch(system),
            item.CanDelete)
        {
            CanUndo = journal.LatestUndoable(JournalId(item), user) is not null,
        };
    }

    private StartupCommandResult Done(StartupItem item, string message) => new(true, message, SafeDescribe(item));

    private StartupCommandResult Failed(StartupItem item, string message) => new(false, message, SafeDescribe(item));

    private static StartupCommandResult Unknown(string itemId) =>
        new(false, $"There is no startup item '{itemId}'. It may have been removed; refresh the list.", null);

    private StartupItemView? SafeDescribe(StartupItem item)
    {
        try
        {
            return Describe(item, system.Startup!.Impact.Read());
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't read startup item {Item}", item.Id);
            return null;
        }
    }
}
