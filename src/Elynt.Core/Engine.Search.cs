using Elynt.Core.Search;
using Elynt.Core.Tweaks;

namespace Elynt.Core;

/// <summary>App-wide search: the sidebar's box, answered in one query.</summary>
public partial interface IEngine
{
    /// <summary>
    /// The tweaks, repair tools and catalogue apps whose name or summary contains the query, best match first.
    /// </summary>
    IReadOnlyList<SearchResult> Search(string query);
}

public sealed partial class Engine
{
    public IReadOnlyList<SearchResult> Search(string query)
    {
        query = query.Trim();
        if (query.Length == 0)
            return [];
        // Only what the pages list: per-game templates but not one game's instance, and no follow-up repair tools,
        // which only a repair result offers.
        var tweaks = catalogue.Tweaks
            .Select(tweak => new SearchResult(SearchResultKind.Tweak, tweak.Id, tweak.Name, tweak.Section, tweak.Summary));
        var repairTools = ListRepairTools()
            .Where(tool => !tool.IsFollowUp)
            .Select(tool => new SearchResult(SearchResultKind.RepairTool, tool.Id, tool.Name, tool.Section, tool.WhatItDoes));
        var apps = ListApps()
            .Select(app => new SearchResult(SearchResultKind.App, app.Id, app.Name, Sections.Apps, app.Description));
        return tweaks.Concat(repairTools).Concat(apps)
            .Select(result => (Result: result, Rank: Rank(result, query)))
            .Where(match => match.Rank is not null)
            .OrderBy(match => match.Rank)
            .Select(match => match.Result)
            // Checked for the matches alone, since it reads the PC.
            .Where(result => result.Kind != SearchResultKind.Tweak || IsListed(catalogue.Get(result.Id)))
            .ToList();
    }

    /// <summary>
    /// Whether <see cref="ListTweaks"/> lists the tweak: a tweak that doesn't fit this PC and asks to be hidden is left
    /// out, unless it still has a change to undo.
    /// </summary>
    private bool IsListed(TweakDefinition tweak) =>
        !tweak.Applicability.Hide ||
        WhyNotApplicable(tweak) is null ||
        (!IsDestructive(tweak) && History.Newest(tweak.Id) is not null);

    /// <summary>
    /// How well a result matches, lower being better: the name starts with the query, then a word in the name does,
    /// then the name has it inside a word, then only the summary has it. Null is no match.
    /// </summary>
    private static int? Rank(SearchResult result, string query)
    {
        var at = result.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
        if (at == 0)
            return 0;
        if (at > 0)
            return StartsAWord(result.Name, query, at) ? 1 : 2;
        if (result.Summary?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true)
            return 3;
        return null;
    }

    /// <summary>Whether the query starts a word of the name, at <paramref name="at"/> (its first match) or later.</summary>
    private static bool StartsAWord(string name, string query, int at)
    {
        for (; at >= 0; at = name.IndexOf(query, at + 1, StringComparison.CurrentCultureIgnoreCase))
        {
            if (!char.IsLetterOrDigit(name[at - 1]))
                return true;
        }
        return false;
    }
}
