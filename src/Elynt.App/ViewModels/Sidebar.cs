using Elynt.Core.Tweaks;

namespace Elynt.App.ViewModels;

/// <summary>One of the sidebar's groups: its small header and its pages, in order. Home's group has no header.</summary>
public sealed record SidebarGroup(string Header, IReadOnlyList<string> Pages);

/// <summary>
/// The sidebar's pages: Home, the PLAY, WINDOWS and TOOLS groups, then Undo history and Settings pinned at the bottom.
/// A new page joins its group with one line here.
/// </summary>
public static class Sidebar
{
    public static IReadOnlyList<SidebarGroup> Groups { get; } =
    [
        new("", [MainViewModel.Home]),
        new("PLAY", [Sections.Gaming, MainViewModel.PerGameProfiles, Sections.Overclocking, Sections.Drivers]),
        new("WINDOWS", [Sections.DebloatAndPrivacy, Sections.Security, Sections.WindowsUpdate, Sections.StartupAndServices]),
        new("TOOLS", [Sections.Apps, Sections.Cleanup, Sections.Repair]),
    ];

    /// <summary>The groups' pages, top to bottom: the part of the sidebar that scrolls when the window is short.</summary>
    public static IReadOnlyList<string> GroupedPages { get; } = [.. Groups.SelectMany(group => group.Pages)];

    /// <summary>Pinned under the groups, above the connection status.</summary>
    public static IReadOnlyList<string> FooterPages { get; } = [MainViewModel.UndoHistory, MainViewModel.Settings];

    /// <summary>Every page, in sidebar order.</summary>
    public static IReadOnlyList<string> Pages { get; } = [.. GroupedPages, .. FooterPages];

    /// <summary>The header of the group a page sits in: "PLAY", or "" for Home and the footer's pages.</summary>
    public static string HeaderOf(string? page) => Groups.FirstOrDefault(group => group.Pages.Contains(page))?.Header ?? "";
}
