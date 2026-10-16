using Elynt.Core.Journal;
using Elynt.Core.Safety;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elynt.Core.Startup;

/// <summary>
/// The Startup page's commands for one user: every startup item with publisher, signature and impact; turning one
/// off or on through the same switch Task Manager uses (journaled, so it shows in Undo history); deleting one
/// (destructive); and spotting items added since the last look.
/// </summary>
internal sealed class StartupManager(
    SystemAdapters system,
    ChangeJournal journal,
    string user,
    SafetyNet? safetyNet,
    TimeProvider clock,
    ILogger? logger)
{
    /// <summary>Journal entries for startup switches use this prefix before the item id instead of a tweak id.</summary>
    public const string JournalPrefix = "startup:";

    private readonly ILogger _log = logger ?? NullLogger.Instance;

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

    private StartupCommandResult Change(StartupItem item, TweakStep step, string message)
    {
        string? note;
        try
        {
            note = safetyNet?.BeforeChange(AsTweak(item, step));
        }
        catch (BackupFailedException ex)
        {
            return Failed(item, $"{ex.Message} Nothing was changed.");
        }

        var record = step.Capture(system);
        var entry = journal.Open(JournalId(item), user, system.System.OsBuild, clock.GetUtcNow(), [record]);
        try
        {
            step.Apply(system);
        }
        catch
        {
            record.Revert(system);
            journal.Close(entry, EntryStatus.RolledBack, clock.GetUtcNow());
            throw;
        }
        journal.Close(entry, EntryStatus.Applied, clock.GetUtcNow());
        _log.LogInformation("Startup item {Item} switched for {User}", item.Id, user);
        return Done(item, note is null ? message : $"{message} {note}");
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

    private static IEnumerable<StartupItem> Read(StartupAdapters startup) =>
        startup.Sources.RunValues().Select(StartupItems.FromRunValue);

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
            item.Publisher ?? signature.Publisher,
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
