using Elynt.App.ViewModels;
using Elynt.Core;
using Elynt.Core.Tweaks;

namespace Elynt.App.Tests;

/// <summary>
/// A tweak's row in the tweak list: what it says beside its labels, and which header it sits under. The switch shows
/// on or off, so the row doesn't say it again; the detail pane still does.
/// </summary>
public class TweakRowTests
{
    [Fact]
    public void A_row_that_is_on_says_nothing_beside_its_labels_while_the_detail_pane_says_on()
    {
        var row = Row(Tweak(TweakState.On));

        Assert.Equal((null, "On"), (row.RowStateText, row.StateText));
    }

    [Fact]
    public void A_row_that_is_off_says_nothing_beside_its_labels_while_the_detail_pane_says_off()
    {
        var row = Row(Tweak(TweakState.Off));

        Assert.Equal((null, "Off"), (row.RowStateText, row.StateText));
    }

    [Fact]
    public void A_row_waiting_for_a_restart_says_so()
    {
        var row = Row(Tweak(TweakState.On) with { PendingReboot = true });

        Assert.Equal("Pending reboot", row.RowStateText);
    }

    [Fact]
    public void A_row_that_does_not_fit_this_PC_says_so()
    {
        var row = Row(Tweak(TweakState.Off) with { NotApplicableReason = "Needs an NVIDIA graphics card." });

        Assert.Equal("Not applicable", row.RowStateText);
    }

    [Fact]
    public void A_per_game_row_says_so()
    {
        var row = Row(Tweak(TweakState.Off) with { PerGame = true });

        Assert.Equal("Per game", row.RowStateText);
    }

    [Fact]
    public void A_row_sits_under_its_tweaks_header()
    {
        var row = Row(Tweak(TweakState.Off) with { Group = "Telemetry" });

        Assert.Equal("Telemetry", row.Group);
    }

    [Fact]
    public void A_possible_placebo_row_sits_in_the_placebo_fold_whatever_its_header()
    {
        var row = Row(Tweak(TweakState.Off) with { Tier = EvidenceTier.PossiblePlacebo, Group = "Network" });

        Assert.Equal("Possible placebo", row.Group);
    }

    [Fact]
    public void A_page_lists_plain_rows_then_each_header_in_the_order_first_listed_then_the_placebo_fold()
    {
        TweakView[] listed =
        [
            Tweak(TweakState.Off, "ads.one") with { Group = "Ads & suggestions" },
            Tweak(TweakState.Off, "plain.one"),
            Tweak(TweakState.Off, "telemetry.not-here") with { Group = "Telemetry", NotApplicableReason = "Needs Windows 11." },
            Tweak(TweakState.Off, "placebo.one") with { Tier = EvidenceTier.PossiblePlacebo },
            Tweak(TweakState.Off, "telemetry.one") with { Group = "Telemetry" },
            Tweak(TweakState.Off, "ads.two") with { Group = "Ads & suggestions" },
            Tweak(TweakState.Off, "plain.not-here") with { NotApplicableReason = "Needs an AMD CPU." },
        ];

        var page = TweakItemViewModel.InPageOrder(listed.Select(Row));

        // Within each group, rows that fit this PC come first.
        Assert.Equal(
            ["plain.one", "plain.not-here", "ads.one", "ads.two", "telemetry.one", "telemetry.not-here", "placebo.one"],
            page.Select(row => row.View.Id));
    }

    // ---- helpers ----

    private static TweakView Tweak(TweakState state, string id = "some.tweak") => new(
        id, Sections.DebloatAndPrivacy, "Some tweak", "Does a thing.", "Measured.", [], EvidenceTier.Proven,
        [@"Set HKCU\Some\Value to 1 (REG_DWORD)"], "Puts it back.", SessionRefresh.None, state, CanUndo: false);

    private static TweakItemViewModel Row(TweakView view) => new(view, (_, _) => Task.CompletedTask);
}
