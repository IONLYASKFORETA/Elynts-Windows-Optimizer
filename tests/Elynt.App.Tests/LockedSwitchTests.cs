using Elynt.App.ViewModels;
using Elynt.Core;
using Elynt.Core.Tweaks;

namespace Elynt.App.Tests;

/// <summary>
/// A tweak's switch that can't be flipped either way, as its row in the tweak list shows it: switched off (not
/// enabled), with a label saying why.
/// </summary>
public class LockedSwitchTests
{
    [Fact]
    public void A_tweak_already_set_on_this_PC_shows_the_label_and_cant_be_switched()
    {
        var row = Row(AlreadySet);

        Assert.Equal((false, "Already set on this PC"), (row.IsEnabled, row.LockText));
    }

    [Fact]
    public void A_tweak_that_does_not_fit_this_PC_but_is_already_set_still_shows_the_label_and_cant_be_switched()
    {
        var row = Row(AlreadySet with { NotApplicableReason = "Needs an NVIDIA graphics card." });

        Assert.Equal((false, "Already set on this PC"), (row.IsEnabled, row.LockText));
    }

    [Fact]
    public void A_blocked_tweak_that_is_already_set_still_shows_the_label_and_cant_be_switched()
    {
        // A blocked tweak that is on can usually be switched off; one already set has nothing of the app's to undo.
        var row = Row(AlreadySet with { BlockedReason = "Turn off Tamper Protection in Windows Security first." });

        Assert.Equal((false, "Already set on this PC"), (row.IsEnabled, row.LockText));
    }

    [Fact]
    public void A_destructive_change_in_place_shows_cant_be_undone_and_cant_be_switched()
    {
        // The app switched it on, but a destructive change has nothing to put back (ADR 0002).
        var row = Row(On(canUndo: false) with { Reversibility = Reversibility.Destructive });

        Assert.Equal((false, "Can't be undone"), (row.IsEnabled, row.LockText));
    }

    // ---- helpers ----

    /// <summary>On because Windows set it: the Engine reports it on, already set, with nothing of the app's to undo.</summary>
    private static TweakView AlreadySet => On(canUndo: false) with { AlreadySet = true };

    private static TweakView On(bool canUndo) => new(
        "some.tweak", Sections.Gaming, "Some tweak", "Does a thing.", "Measured.", [], EvidenceTier.Proven,
        [@"Set HKCU\Some\Value to 1 (REG_DWORD)"], "Puts it back.", SessionRefresh.None, TweakState.On, canUndo);

    private static TweakItemViewModel Row(TweakView view) => new(view, (_, _) => Task.CompletedTask);
}
