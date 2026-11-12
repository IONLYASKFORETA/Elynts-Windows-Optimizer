using Elynt.Core.Tweaks;

namespace Elynt.Core;

/// <summary>What setting a pick-one choice did.</summary>
/// <param name="Tweaks">Every option's tweak as it stands now, in the choice's order, so the row shows which one is on.</param>
public sealed record ChoiceResult(bool Succeeded, string Message, IReadOnlyList<TweakView> Tweaks);

/// <summary>Pick-one choices (the power plan, the monthly update delay): tweaks that fight each other, set as one.</summary>
public partial interface IEngine
{
    /// <summary>
    /// Sets a pick-one choice to one of its options, or to its default with a null <paramref name="tweakId"/>. The
    /// app's change for the option that was on is undone first, then the chosen one is applied; if that fails, the
    /// option that was on is put back. The default only undoes the app's change, so an option that Windows or another
    /// tool set stays on. Each option keeps its own change in the Undo history.
    /// </summary>
    ChoiceResult Choose(string choiceId, string? tweakId, ApplyOptions? options = null);
}

public sealed partial class Engine
{
    public ChoiceResult Choose(string choiceId, string? tweakId, ApplyOptions? options = null)
    {
        if (catalogue.Choice(choiceId) is not { } choice)
            return new(false, $"There is no choice called '{choiceId}'.", []);
        if (tweakId is not null && choice.Options.All(option => option.TweakId != tweakId))
            return Answer(choice, false, $"'{tweakId}' isn't one of the {choice.Name} options. Nothing was changed.");

        var applyOptions = options ?? ApplyOptions.Default;
        var wording = ChangeWording.Choice(choice, tweakId);
        var chosen = tweakId is null ? null : catalogue.Get(tweakId);
        // Refused before anything is undone, so a refusal leaves the option that was on as it is.
        if (chosen is not null && Refusal(chosen, applyOptions) is { } refused)
            return Answer(choice, refused);

        var on = choice.Options.Select(option => catalogue.Get(option.TweakId)).FirstOrDefault(t => SafeDetect(t) == TweakState.On);
        // On with no change of the app's: Windows or another tool set it, so there is nothing to undo (ADR 0002).
        var setElsewhere = on is not null && History.Newest(on.Id) is null;

        if (chosen is null)
        {
            if (on is null)
                return Answer(choice, true, $"Already set to {choice.DefaultLabel}. Nothing was changed.");
            if (setElsewhere)
                return Answer(choice, false,
                    $"{choice.LabelOf(on.Id)} was set by Windows or another tool, not by the app, so the app has nothing of its own to undo. Nothing was changed.");
            return Answer(choice, UndoTweak(on.Id, wording));
        }

        // Nothing of the app's to undo first: the chosen option goes on (or over the one Windows set, which its undo puts back).
        if (on is null || on.Id == chosen.Id || setElsewhere)
            return Answer(choice, ApplyTweak(chosen, applyOptions, wording: wording));

        // Undone before the new option goes on: afterwards, its undo would find the setting changed and refuse, or put
        // its own before-state back over the new option.
        var before = choice.LabelOf(on.Id);
        var undone = UndoTweak(on.Id, wording);
        if (!undone.Succeeded)
            return Answer(choice, false, $"{wording.CouldNot}, because the app's change for {before} couldn't be undone first. {undone.Message}");

        var applied = ApplyTweak(chosen, applyOptions, wording: wording);
        if (applied.Succeeded)
            return Answer(choice, applied);
        // The user chose this option before, so its warning was read then.
        var back = ApplyTweak(on, applyOptions with { ConfirmWarning = true }, wording: ChangeWording.Choice(choice, on.Id));
        return Answer(choice, false, back.Succeeded
            ? $"{applied.Message} {before} is back on."
            : $"{applied.Message} {before} couldn't be put back either: {back.Message}");
    }

    private ChoiceResult Answer(ChoiceGroup choice, CommandResult result) => Answer(choice, result.Succeeded, result.Message);

    private ChoiceResult Answer(ChoiceGroup choice, bool succeeded, string message) => new(
        succeeded,
        message,
        choice.Options.Select(option => SafeDescribe(catalogue.Get(option.TweakId))).OfType<TweakView>().Where(Listed).ToList());
}
