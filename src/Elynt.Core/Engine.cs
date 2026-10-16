using Elynt.Core.Apps;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elynt.Core;

public enum TweakState
{
    Off,
    On,
}

/// <summary>A tweak as the UI shows it: its description plus its real, detected state.</summary>
public sealed record TweakView(
    string Id,
    string Section,
    string Name,
    string WhatItDoes,
    string Evidence,
    IReadOnlyList<Source> Sources,
    EvidenceTier Tier,
    IReadOnlyList<string> ExactChange,
    string Undo,
    SessionRefresh Refresh,
    TweakState State,
    bool CanUndo);

public sealed record CommandResult(bool Succeeded, string Message, TweakView? Tweak);

/// <summary>
/// The service's command surface. The pipe mirrors these commands one-to-one, and tests drive the app through
/// them, so this is the single test seam.
/// </summary>
public interface IEngine
{
    IReadOnlyList<TweakView> ListTweaks();
    CommandResult Apply(string tweakId);
    CommandResult Revert(string tweakId);
    IReadOnlyList<AppView> ListApps();
}

/// <summary>Runs commands for one user, composing the catalogue, the journal and the system adapters.</summary>
public sealed class Engine(
    Catalogue catalogue,
    SystemAdapters system,
    ChangeJournal journal,
    string user,
    TimeProvider? clock = null,
    ILogger<Engine>? logger = null,
    AppCatalogue? apps = null) : IEngine
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _log = logger ?? NullLogger<Engine>.Instance;
    private readonly AppInstaller _apps = new(apps ?? AppCatalogue.Default);

    public IReadOnlyList<AppView> ListApps() => _apps.ListApps();

    public IReadOnlyList<TweakView> ListTweaks() => catalogue.Tweaks.Select(Describe).ToList();

    public CommandResult Apply(string tweakId)
    {
        if (!catalogue.TryGet(tweakId, out var tweak))
            return Unknown(tweakId);

        try
        {
            if (Detect(tweak) == TweakState.On)
                return Done(tweak, "Already on. Nothing was changed.");

            // Write-ahead: the exact prior state is on disk before anything changes.
            List<StepRecord> records;
            JournalEntry entry;
            try
            {
                records = tweak.Steps.Select(step => step.Capture(system)).ToList();
                entry = journal.Open(tweak.Id, user, system.System.OsBuild, _clock.GetUtcNow(), records);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Couldn't record the prior state of {Tweak}", tweak.Id);
                return Failed(tweak, $"Couldn't save the current settings before changing them: {ex.Message} Nothing was changed.");
            }

            try
            {
                foreach (var step in tweak.Steps)
                    step.Apply(system);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Applying {Tweak} failed; putting back the prior state", tweak.Id);
                RevertSteps(records);
                journal.Close(entry, EntryStatus.RolledBack, _clock.GetUtcNow());
                return Failed(tweak, $"Couldn't turn this on: {ex.Message} Nothing was changed.");
            }

            journal.Close(entry, EntryStatus.Applied, _clock.GetUtcNow());
            _log.LogInformation("Applied {Tweak} for {User}", tweak.Id, user);
            return Done(tweak, "Turned on.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Applying {Tweak} failed and could not be fully put back", tweak.Id);
            return Failed(tweak, $"Couldn't turn this on: {ex.Message} The prior state is saved; switch it off to undo.");
        }
    }

    public CommandResult Revert(string tweakId)
    {
        if (!catalogue.TryGet(tweakId, out var tweak))
            return Unknown(tweakId);

        var entry = journal.LatestUndoable(tweak.Id, user);
        if (entry is null)
            return Failed(tweak, "Nothing to undo: this was already set before the app changed anything.");

        try
        {
            RevertSteps(entry.Steps);
            journal.CloseReverted(entry, _clock.GetUtcNow());
            _log.LogInformation("Reverted {Tweak} for {User}", tweak.Id, user);
            return Done(tweak, "Turned off. The previous settings are back.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Reverting {Tweak} failed", tweak.Id);
            return Failed(tweak, $"Couldn't undo this: {ex.Message}");
        }
    }

    private void RevertSteps(IReadOnlyList<StepRecord> records)
    {
        for (var i = records.Count - 1; i >= 0; i--)
            records[i].Revert(system);
    }

    private TweakState Detect(TweakDefinition tweak) =>
        tweak.Steps.All(step => step.IsApplied(system)) ? TweakState.On : TweakState.Off;

    private TweakView Describe(TweakDefinition tweak) => new(
        tweak.Id,
        tweak.Section,
        tweak.Name,
        tweak.WhatItDoes,
        tweak.Evidence,
        tweak.Sources,
        tweak.Tier,
        tweak.Steps.Select(step => step.DescribeChange()).ToList(),
        tweak.Undo,
        tweak.Refresh,
        Detect(tweak),
        journal.LatestUndoable(tweak.Id, user) is not null);

    private CommandResult Done(TweakDefinition tweak, string message) => new(true, message, Describe(tweak));

    private CommandResult Failed(TweakDefinition tweak, string message) => new(false, message, SafeDescribe(tweak));

    private static CommandResult Unknown(string tweakId) => new(false, $"There is no tweak called '{tweakId}'.", null);

    private TweakView? SafeDescribe(TweakDefinition tweak)
    {
        try
        {
            return Describe(tweak);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't read the state of {Tweak}", tweak.Id);
            return null;
        }
    }
}
