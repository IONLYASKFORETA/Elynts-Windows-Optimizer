using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Elynt.App;

/// <summary>
/// The app's own dialog, in place of Windows' message boxes: the same title, wording, buttons and answers, drawn in
/// the app's look. The safe choice is the default button and has the keyboard focus; Escape or closing the dialog
/// answers it too. Shown modally over its owner, centred on it, with no taskbar entry of its own.
/// </summary>
public partial class ConfirmDialog : Window
{
    private readonly Confirmation _confirmation;

    /// <param name="owner">The window it is shown over, or null when there is none yet (it is then centred on the screen).</param>
    public ConfirmDialog(Window? owner, string message, string title, ConfirmButtons buttons, ConfirmRisk risk = ConfirmRisk.Ordinary)
    {
        _confirmation = new Confirmation(buttons, risk);
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        // A long message scrolls rather than pushing the buttons off the screen.
        MaxHeight = SystemParameters.WorkArea.Height * 0.8;
        if (owner is { IsVisible: true })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            // Nothing to sit over (before the window opens, or while it is in the tray): findable on the taskbar.
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
        }

        Button? safe = null;
        foreach (var choice in _confirmation.Choices)
        {
            var button = new Button
            {
                Content = choice.Text,
                MinWidth = 96,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = choice.IsSafe,
            };
            if (choice.Look == ChoiceLook.Plain)
                button.Padding = new Thickness(18, 7, 18, 7); // as tall as the primary button next to it, with its edge
            else
                button.Style = (Style)FindResource(choice.Look == ChoiceLook.Destructive ? "Button.Destructive" : "Button.Primary");
            button.Click += (_, _) =>
            {
                _confirmation.Press(choice);
                Close();
            };
            Buttons.Children.Add(button);
            if (choice.IsSafe)
                safe = button;
        }
        Loaded += (_, _) => safe?.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            Close();
        };
        // No title bar, so the dialog moves by dragging anywhere that isn't a button.
        MouseLeftButtonDown += (_, _) =>
        {
            if (Mouse.LeftButton == MouseButtonState.Pressed)
                DragMove();
        };
    }

    /// <summary>The button pressed, or the safe choice when the dialog was closed any other way.</summary>
    public ConfirmAnswer Answer => _confirmation.Answer;

    /// <summary>Shows the dialog modally and returns the answer.</summary>
    public ConfirmAnswer Ask()
    {
        ShowDialog();
        return Answer;
    }

    /// <summary>Asks a question over <paramref name="owner"/> and returns the answer.</summary>
    public static ConfirmAnswer Ask(Window? owner, string message, string title, ConfirmButtons buttons, ConfirmRisk risk) =>
        new ConfirmDialog(owner, message, title, buttons, risk).Ask();

    /// <summary>Shows a note with a single OK over <paramref name="owner"/>.</summary>
    public static void Tell(Window? owner, string message, string title) =>
        new ConfirmDialog(owner, message, title, ConfirmButtons.Ok).Ask();
}
