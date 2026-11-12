using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>
/// The one-line summary under each tweak's name and the header it sits under (spec #103, "Tweak descriptions"),
/// driven through the Engine.
/// </summary>
public class TweakSummaryTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();

    [Fact]
    public void A_tweak_is_listed_with_its_summary_and_its_group()
    {
        var tweak = LabelTests.TestTweak("test.summary") with
        {
            Summary = "Stops Windows doing a thing you don't need.",
            Group = "Telemetry",
        };

        var view = Assert.Single(EngineFor(new Catalogue([tweak])).ListTweaks());

        Assert.Equal(("Stops Windows doing a thing you don't need.", "Telemetry"), (view.Summary, view.Group));
    }

    /// <summary>Every catalogue tweak and per-game template, plus one game's instance of each per-game tweak.</summary>
    public static TheoryData<string> EveryCatalogueTweak()
    {
        var ids = new TheoryData<string>();
        foreach (var tweak in Catalogue.Default.Tweaks)
        {
            ids.Add(tweak.Id);
            if (tweak.PerGame)
                ids.Add($"{tweak.Id}{PerGameTweak.Separator}{GameExe}");
        }
        return ids;
    }

    [Theory]
    [MemberData(nameof(EveryCatalogueTweak))]
    public void Every_catalogue_tweak_has_a_one_sentence_summary(string id)
    {
        Assert.True(Catalogue.Default.TryGet(id, out var tweak));

        var summary = tweak.Summary;

        Assert.False(string.IsNullOrWhiteSpace(summary), $"{id} has no summary.");
        Assert.EndsWith(".", summary);
        // One sentence: no full stop, question or exclamation mark followed by more text.
        Assert.DoesNotMatch(@"[.!?]\s", summary);
        // Short enough for one line beside the switch at the window's default size; it wraps on a smaller one.
        Assert.InRange(summary.Length, 1, 120);
    }

    public static TheoryData<string, string> DebloatGroups() => new()
    {
        { "privacy.diagnostic-data-minimum", "Telemetry" },
        { "privacy.diagtrack-service-off", "Telemetry" },
        { "privacy.feedback-prompts-off", "Telemetry" },
        { "privacy.tailored-experiences-off", "Telemetry" },
        { "debloat.start-recommendations-off", "Ads & suggestions" },
        { "debloat.lock-screen-tips-off", "Ads & suggestions" },
        { "debloat.settings-suggestions-off", "Ads & suggestions" },
        { "debloat.explorer-promos-off", "Ads & suggestions" },
        { "debloat.bing-in-start-search-off", "Ads & suggestions" },
        { "debloat.widgets-off", "Ads & suggestions" },
        { "debloat.edge-nags-off", "Ads & suggestions" },
        { "debloat.edge-background-off", "Built-in apps" },
        { "debloat.game-dvr-background-capture-off", "Built-in apps" },
        { "debloat.remove-onedrive", "Built-in apps" },
        { "debloat.remove-store", "Built-in apps" },
        { "debloat.remove-app.clipchamp.clipchamp", "Built-in apps" },
        { "debloat.remove-app.microsoft.windowsfeedbackhub", "Built-in apps" },
        { "debloat.deprovision-preinstalled", "Built-in apps" },
        { "privacy.recall-off", "AI features" },
        { "privacy.remove-recall", "AI features" },
        { "debloat.remove-copilot", "AI features" },
    };

    [Theory]
    [MemberData(nameof(DebloatGroups))]
    public void A_debloat_and_privacy_tweak_sits_under_its_header(string id, string group)
    {
        var view = DebloatAndPrivacy().Single(t => t.Id == id);

        Assert.Equal(group, view.Group);
    }

    [Fact]
    public void Debloat_and_privacy_lists_four_headers_in_order_and_every_row_sits_under_one()
    {
        var rows = DebloatAndPrivacy();

        // The window shows groups in the order the list first names them.
        Assert.Equal(["Telemetry", "Ads & suggestions", "Built-in apps", "AI features"], rows.Select(t => t.Group).Distinct());
    }

    // ---- helpers ----

    private const string GameExe = @"C:\Games\Fortnite\FortniteClient-Win64-Shipping.exe";

    private readonly FakeOptionalFeatures _features = new();

    private Engine EngineFor(Catalogue catalogue) =>
        new(catalogue, new SystemAdapters(_registry, _system) { Features = _features },
            new ChangeJournal(new InMemoryJournalStore()), "S-1-5-21-test");

    private Engine NewEngine() => EngineFor(Catalogue.Default);

    /// <summary>The Debloat &amp; Privacy page's rows on a Copilot+ PC, so Recall's removal is listed too.</summary>
    private IReadOnlyList<TweakView> DebloatAndPrivacy()
    {
        _features.Add("Recall", enabled: true);
        return NewEngine().ListTweaks().Where(t => t.Section == Sections.DebloatAndPrivacy).ToList();
    }
}
