namespace Elynt.App.ViewModels;

/// <summary>One segment of a pick-one row's choice: one option's tweak, or the choice's default.</summary>
/// <param name="TweakId">The option's tweak, or null for the default.</param>
/// <param name="IsSelected">The option in place: the one that is on, or the default while none is.</param>
/// <param name="IsEnabled">
/// Whether it can be chosen: an option that doesn't fit this PC can't, nor can the default while the option that is on
/// was set by Windows or another tool, as the app has nothing of its own to undo.
/// </param>
public sealed class ChoiceOptionViewModel(TweakItemViewModel row, string? tweakId, string label, bool isSelected, bool isEnabled)
{
    public string? TweakId { get; } = tweakId;

    public string Label { get; } = label;

    public bool IsSelected { get; } = isSelected;

    public bool IsEnabled { get; } = isEnabled;

    /// <summary>Sends this choice, unless it is already the one in place or can't be chosen.</summary>
    public Task ChooseAsync() => row.ChooseAsync(this);
}
