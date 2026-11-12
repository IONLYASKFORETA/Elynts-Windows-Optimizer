using Elynt.App.ViewModels;
using Elynt.Core;
using Elynt.Core.Tweaks;

namespace Elynt.App.Tests;

/// <summary>
/// A pick-one row: the options of a choice such as the power plan share one row, set with a segmented choice instead
/// of a switch. The default is selected while no option is on; an option Windows set shows as already set.
/// </summary>
public class PickOneRowTests
{
    private static readonly ChoiceGroup PowerPlan = new(
        "gaming.power-plan", "Power plan", "Which plan Windows runs.", "Windows default",
        [new("gaming.high", "High performance"), new("gaming.ultimate", "Ultimate")]);

    private readonly List<string?> _chosen = [];

    [Fact]
    public void A_choices_options_share_one_row_named_after_the_choice_where_the_first_option_is_listed()
    {
        var rows = Rows(Tweak("gaming.plain"), Option("gaming.high"), Tweak("gaming.other"), Option("gaming.ultimate"));

        Assert.Equal(["Some tweak", "Power plan", "Some tweak"], rows.Select(r => r.Name));
        var row = rows[1];
        Assert.Equal("Which plan Windows runs.", row.Summary);
        Assert.True(row.IsChoice);
        Assert.False(row.HasSwitch);
        Assert.Equal(["Windows default", "High performance", "Ultimate"], row.Choices.Select(c => c.Label));
    }

    [Fact]
    public void With_no_option_on_the_default_is_selected_and_the_row_shows_the_first_options_details()
    {
        var row = PowerPlanRow(Option("gaming.high"), Option("gaming.ultimate"));

        Assert.Equal(["Windows default"], row.Choices.Where(c => c.IsSelected).Select(c => c.Label));
        Assert.Equal("gaming.high", row.View.Id);
    }

    [Fact]
    public void The_option_that_is_on_is_selected_and_the_row_shows_its_details()
    {
        var row = PowerPlanRow(Option("gaming.high"), Option("gaming.ultimate", TweakState.On) with { CanUndo = true });

        Assert.Equal(["Ultimate"], row.Choices.Where(c => c.IsSelected).Select(c => c.Label));
        Assert.Equal("gaming.ultimate", row.View.Id);
        Assert.All(row.Choices, c => Assert.True(c.IsEnabled));
        Assert.Null(row.LockText);
    }

    [Fact]
    public void An_option_Windows_set_shows_as_already_set_and_only_the_other_options_can_be_chosen()
    {
        var row = PowerPlanRow(Option("gaming.high", TweakState.On) with { AlreadySet = true }, Option("gaming.ultimate"));

        Assert.Equal("Already set on this PC", row.LockText);
        Assert.Equal([("Windows default", false), ("High performance", true), ("Ultimate", true)],
            row.Choices.Select(c => (c.Label, c.IsEnabled)));
        Assert.True(row.IsEnabled);
    }

    [Fact]
    public void The_row_keeps_its_options_hardware_warnings()
    {
        const string X3d = "On a dual-CCD Ryzen X3D CPU this makes games slower: core parking needs Balanced.";

        var row = PowerPlanRow(Option("gaming.high") with { Warnings = [X3d] }, Option("gaming.ultimate") with { Warnings = [X3d] });

        Assert.True(row.HasWarnings);
        Assert.Equal([X3d], row.Warnings);
    }

    [Fact]
    public void An_option_that_does_not_fit_this_PC_cant_be_chosen()
    {
        var row = PowerPlanRow(Option("gaming.high"), Option("gaming.ultimate") with { NotApplicableReason = "Needs a desktop." });

        Assert.False(row.Choices.Single(c => c.Label == "Ultimate").IsEnabled);
    }

    [Fact]
    public async Task Choosing_sends_the_options_id_the_default_sends_none_and_the_option_in_place_sends_nothing()
    {
        var row = PowerPlanRow(Option("gaming.high", TweakState.On) with { CanUndo = true }, Option("gaming.ultimate"));

        foreach (var choice in row.Choices)
            await choice.ChooseAsync();

        Assert.Equal([null, "gaming.ultimate"], _chosen);
    }

    [Fact]
    public void The_answer_to_a_choice_shows_on_the_row()
    {
        var row = PowerPlanRow(Option("gaming.high", TweakState.On) with { CanUndo = true }, Option("gaming.ultimate"));

        row.ShowChoice([Option("gaming.high"), Option("gaming.ultimate", TweakState.On) with { CanUndo = true }]);

        Assert.Equal(["Ultimate"], row.Choices.Where(c => c.IsSelected).Select(c => c.Label));
        Assert.Equal("gaming.ultimate", row.View.Id);
    }

    // ---- helpers ----

    private static TweakView Tweak(string id, TweakState state = TweakState.Off) => new(
        id, Sections.Gaming, "Some tweak", "Does a thing.", "Measured.", [], EvidenceTier.Situational,
        [@"Set HKCU\Some\Value to 1 (REG_DWORD)"], "Puts it back.", SessionRefresh.None, state, CanUndo: false);

    private static TweakView Option(string id, TweakState state = TweakState.Off) => Tweak(id, state) with { Choice = PowerPlan };

    private IReadOnlyList<TweakItemViewModel> Rows(params TweakView[] views) =>
        TweakItemViewModel.Rows(views, (_, _) => Task.CompletedTask, (_, id) =>
        {
            _chosen.Add(id);
            return Task.CompletedTask;
        });

    private TweakItemViewModel PowerPlanRow(params TweakView[] options) => Assert.Single(Rows(options));
}
