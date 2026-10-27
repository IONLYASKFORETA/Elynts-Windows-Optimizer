namespace Elynt.Core;

/// <summary>
/// How the Engine words a change's result for the page that asked for it. Tweaks, startup switches and service start
/// types all go through the same apply and undo; only the words differ, so pages never rewrite the Engine's messages.
/// </summary>
/// <param name="Done">After a change was made, e.g. "Turned on."</param>
/// <param name="AlreadyDone">When nothing needed changing.</param>
/// <param name="CouldNot">Before the reason a change failed, e.g. "Couldn't turn this on".</param>
/// <param name="UndoHint">How to undo a change that failed half-way, e.g. "switch it off to undo".</param>
/// <param name="Undone">After an undo.</param>
/// <param name="NothingToUndo">When the app never made this change.</param>
/// <param name="ChangeAgain">How to take a fresh copy when undo found something else changed it, or null to say nothing.</param>
internal sealed record ChangeWording(
    string Done,
    string AlreadyDone,
    string CouldNot,
    string UndoHint,
    string Undone,
    string NothingToUndo,
    string? ChangeAgain)
{
    /// <summary>Before the reason an undo failed.</summary>
    public string CouldNotUndo { get; init; } = "Couldn't undo this";

    public static ChangeWording Tweak { get; } = new(
        "Turned on.",
        "Already on. Nothing was changed.",
        "Couldn't turn this on",
        "switch it off to undo",
        "Turned off. The previous settings are back.",
        "Nothing to undo: this was already set before the app changed anything.",
        "Switch the tweak on again");

    /// <summary>A startup item's switch. The Startup page says which way it went, so it sets <see cref="Done"/> each time.</summary>
    public static ChangeWording StartupSwitch { get; } = new(
        "Switched.",
        "Already set. Nothing was changed.",
        "Couldn't change this",
        "undo it from Undo history",
        "Undone. The previous setting is back.",
        "Nothing to undo: the app hasn't switched this startup item.",
        null);
}
