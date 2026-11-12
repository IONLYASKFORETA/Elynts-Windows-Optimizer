using Elynt.Core.Search;

namespace Elynt.App.ViewModels;

/// <summary>
/// A page to open and, optionally, the row to select on it: what the sidebar's search opens, and what a link to a
/// tweak, repair tool or app from another page opens. The window's model opens it.
/// </summary>
/// <param name="Page">A sidebar page, as <see cref="MainViewModel.Pages"/> names it.</param>
/// <param name="RowId">A tweak id, repair tool id or catalogue app id on that page, or null for the page alone.</param>
public sealed record PageTarget(string Page, string? RowId = null)
{
    public static PageTarget Of(SearchResult result) => new(result.Page, result.Id);
}
