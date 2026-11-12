namespace Elynt.Core.Search;

/// <summary>What a search result is, which says which list on its page holds its row.</summary>
public enum SearchResultKind
{
    Tweak,
    RepairTool,
    App,
}

/// <summary>One thing the sidebar's search found, and where it lives: the page to open and the row to select there.</summary>
/// <param name="Id">The row's id on its page: a tweak id, a repair tool id or a catalogue app's winget id.</param>
/// <param name="Page">The sidebar page that lists it (a section name such as "Gaming", "Repair" or "Apps").</param>
/// <param name="Summary">The line under the name: a tweak's summary, what a repair tool does, or an app's description.</param>
public sealed record SearchResult(SearchResultKind Kind, string Id, string Name, string Page, string? Summary);
