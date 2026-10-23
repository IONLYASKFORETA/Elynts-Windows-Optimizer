namespace Elynt.App;

/// <summary>The buttons a confirmation offers: the sets the Windows message boxes it replaced offered.</summary>
public enum ConfirmButtons
{
    Ok,
    OkCancel,
    YesNo,
}

/// <summary>What a confirmation answers: the button pressed.</summary>
public enum ConfirmAnswer
{
    Ok,
    Cancel,
    Yes,
    No,
}

/// <summary>What going ahead does: something ordinary, or something that can't be undone or is risky.</summary>
public enum ConfirmRisk
{
    Ordinary,
    Destructive,
}

/// <summary>How a button is drawn: the theme's plain, primary (the accent wave) or destructive (red) button.</summary>
public enum ChoiceLook
{
    Plain,
    Primary,
    Destructive,
}

/// <summary>
/// One button of a confirmation: its text, the answer it gives and how it is drawn. The safe choice is the default
/// button, has the keyboard focus, and is the answer when the dialog is closed or Escape is pressed.
/// </summary>
public sealed record ConfirmChoice(string Text, ConfirmAnswer Answer, ChoiceLook Look, bool IsSafe);

/// <summary>
/// One question put to the user in the in-app dialog: its buttons, left to right as Windows' message boxes had them,
/// and the answer. Needs no WPF, so it is tested on its own; <see cref="ConfirmDialog"/> draws it.
/// </summary>
public sealed class Confirmation
{
    public Confirmation(ConfirmButtons buttons, ConfirmRisk risk = ConfirmRisk.Ordinary)
    {
        Choices = buttons switch
        {
            ConfirmButtons.Ok => [new("OK", ConfirmAnswer.Ok, ChoiceLook.Primary, IsSafe: true)],
            ConfirmButtons.OkCancel =>
            [
                new("OK", ConfirmAnswer.Ok, GoAheadLook(risk), IsSafe: false),
                new("Cancel", ConfirmAnswer.Cancel, ChoiceLook.Plain, IsSafe: true),
            ],
            ConfirmButtons.YesNo =>
            [
                new("Yes", ConfirmAnswer.Yes, GoAheadLook(risk), IsSafe: false),
                new("No", ConfirmAnswer.No, ChoiceLook.Plain, IsSafe: true),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(buttons)),
        };
        Answer = Choices.Single(c => c.IsSafe).Answer;
    }

    public IReadOnlyList<ConfirmChoice> Choices { get; }

    /// <summary>The button pressed; the safe choice until one is, so closing the dialog any other way gives that.</summary>
    public ConfirmAnswer Answer { get; private set; }

    public void Press(ConfirmChoice choice) => Answer = choice.Answer;

    private static ChoiceLook GoAheadLook(ConfirmRisk risk) =>
        risk == ConfirmRisk.Destructive ? ChoiceLook.Destructive : ChoiceLook.Primary;
}
