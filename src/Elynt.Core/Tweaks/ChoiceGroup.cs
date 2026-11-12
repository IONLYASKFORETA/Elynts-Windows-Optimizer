namespace Elynt.Core.Tweaks;

/// <summary>
/// A pick-one choice: catalogue tweaks that fight each other (the power plans, the monthly update delays), of which at
/// most one is on. It shows as one row with a segmented choice, its default first. Each option stays a tweak of its
/// own, with its own change in the Undo history.
/// </summary>
/// <param name="Name">The row's name, e.g. "Power plan".</param>
/// <param name="Summary">One plain sentence under the name, as a tweak's row has.</param>
/// <param name="DefaultLabel">The choice that keeps no change of the app's, e.g. "Windows default".</param>
/// <param name="Options">The tweaks to choose from, in the order the row shows them after the default.</param>
public sealed record ChoiceGroup(string Id, string Name, string Summary, string DefaultLabel, IReadOnlyList<ChoiceOption> Options)
{
    /// <summary>The option's label, or the default's for a null id.</summary>
    public string LabelOf(string? tweakId) => tweakId is null
        ? DefaultLabel
        : Options.FirstOrDefault(option => option.TweakId == tweakId)?.Label ?? tweakId;
}

/// <summary>One option of a <see cref="ChoiceGroup"/>.</summary>
/// <param name="Label">Its short name on the segmented choice, e.g. "Ultimate".</param>
public sealed record ChoiceOption(string TweakId, string Label);
