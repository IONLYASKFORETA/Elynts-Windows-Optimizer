using Elynt.Core;

namespace Elynt.App.ViewModels;

/// <summary>
/// One row of the Undo history: when, which tweak, where it stands, and an Undo button when it can be undone. A
/// destructive change that is in place says it can't be undone instead, with no Undo button.
/// </summary>
public sealed class HistoryItemViewModel(ChangeView change) : ObservableObject
{
    private string? _message;
    private bool _isBusy;

    public ChangeView Change { get; } = change;

    public string When => Change.ChangedAt.ToLocalTime().ToString("g");

    public string TweakName => Change.TweakName;

    public string StateText => Change.State switch
    {
        ChangeState.Applied => "Applied",
        ChangeState.Interrupted when Change.Destructive => "Interrupted",
        ChangeState.Interrupted => "Interrupted (can be undone)",
        ChangeState.Failed => "Failed and put back",
        ChangeState.Undone when Change.UndoneAt is { } at => $"Undone {at.ToLocalTime():g}",
        _ => "Undone",
    };

    /// <summary>
    /// Why this row has no Undo button, in the same words as the tweak's locked switch, or null when it has one. Only a
    /// destructive change still in place has nothing to put back (ADR 0002).
    /// </summary>
    public string? LockText =>
        Change is { Destructive: true, State: ChangeState.Applied or ChangeState.Interrupted } ? TweakItemViewModel.CantBeUndone : null;

    public bool IsLocked => LockText is not null;

    /// <summary>The row shows an Undo button; a locked one shows <see cref="LockText"/> instead.</summary>
    public bool HasUndoButton => !IsLocked;

    public bool CanUndo => Change.CanUndo && !IsBusy;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (Set(ref _isBusy, value))
                OnPropertyChanged(nameof(CanUndo));
        }
    }

    /// <summary>Why the last undo of this row didn't happen, e.g. something else changed the value since.</summary>
    public string? Message
    {
        get => _message;
        set => Set(ref _message, value);
    }
}
