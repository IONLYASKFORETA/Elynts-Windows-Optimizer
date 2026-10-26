namespace Elynt.App.Tests;

/// <summary>
/// The in-app confirmation that replaced Windows' message boxes: the same buttons and answers, with the safe choice as
/// the default and what can't be undone in red.
/// </summary>
public class ConfirmationTests
{
    [Fact]
    public void A_destructive_yes_no_question_draws_yes_in_red_and_makes_no_the_safe_choice()
    {
        var confirmation = new Confirmation(ConfirmButtons.YesNo, ConfirmRisk.Destructive);

        Assert.Equal(
            [
                new ConfirmChoice("Yes", ConfirmAnswer.Yes, ChoiceLook.Destructive, IsSafe: false),
                new ConfirmChoice("No", ConfirmAnswer.No, ChoiceLook.Plain, IsSafe: true),
            ],
            confirmation.Choices);
    }

    [Fact]
    public void Closing_the_dialog_without_pressing_a_button_answers_no()
    {
        var confirmation = new Confirmation(ConfirmButtons.YesNo, ConfirmRisk.Destructive);

        Assert.Equal(ConfirmAnswer.No, confirmation.Answer);
    }

    [Fact]
    public void Pressing_yes_answers_yes()
    {
        var confirmation = new Confirmation(ConfirmButtons.YesNo, ConfirmRisk.Destructive);

        confirmation.Press(confirmation.Choices[0]);

        Assert.Equal(ConfirmAnswer.Yes, confirmation.Answer);
    }

    [Fact]
    public void An_ordinary_yes_no_question_draws_yes_as_the_primary_button()
    {
        var confirmation = new Confirmation(ConfirmButtons.YesNo, ConfirmRisk.Ordinary);

        Assert.Equal(
            [
                new ConfirmChoice("Yes", ConfirmAnswer.Yes, ChoiceLook.Primary, IsSafe: false),
                new ConfirmChoice("No", ConfirmAnswer.No, ChoiceLook.Plain, IsSafe: true),
            ],
            confirmation.Choices);
    }

    [Fact]
    public void An_ok_cancel_question_makes_cancel_the_safe_choice()
    {
        var confirmation = new Confirmation(ConfirmButtons.OkCancel, ConfirmRisk.Destructive);

        Assert.Equal(
            [
                new ConfirmChoice("OK", ConfirmAnswer.Ok, ChoiceLook.Destructive, IsSafe: false),
                new ConfirmChoice("Cancel", ConfirmAnswer.Cancel, ChoiceLook.Plain, IsSafe: true),
            ],
            confirmation.Choices);
    }

    [Fact]
    public void Closing_an_ok_cancel_question_answers_cancel()
    {
        var confirmation = new Confirmation(ConfirmButtons.OkCancel, ConfirmRisk.Ordinary);

        Assert.Equal(ConfirmAnswer.Cancel, confirmation.Answer);
    }

    [Fact]
    public void A_note_has_a_single_ok_that_closing_it_also_answers()
    {
        var note = new Confirmation(ConfirmButtons.Ok);

        Assert.Equal([new ConfirmChoice("OK", ConfirmAnswer.Ok, ChoiceLook.Primary, IsSafe: true)], note.Choices);
        Assert.Equal(ConfirmAnswer.Ok, note.Answer);
    }
}
