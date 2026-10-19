using System.Text.RegularExpressions;
using Elynt.Core.Apps;

namespace Elynt.Windows;

/// <summary>
/// Reads the tables winget's command line prints for <c>search</c> and <c>list</c>: a header row, a line of dashes,
/// then rows padded to fixed column widths. Columns are found from where each header word starts, and picked by
/// position (Name, Id, Version first; Source last) so translated headers read the same. A long name or id is cut
/// off with "…"; rows whose id was cut off are dropped, since the id can't be used.
/// </summary>
public static partial class WingetTable
{
    public static IReadOnlyList<PackageInfo> ParseSearch(string output) =>
        Rows(output)
            .Where(r => r.Length >= 3)
            .Select(r => new PackageInfo(r[1], r[0], r[2]))
            .ToList();

    public static IReadOnlyList<InstalledPackage> ParseList(string output) =>
        Rows(output)
            .Where(r => r.Length >= 3)
            .Select(r => new InstalledPackage(r[1], r[0], r[2],
                // Name, Id, Version, Available, Source: the Available column is only there when an update is.
                r.Length >= 5 ? Blank(r[3]) : null,
                r.Length >= 4 ? Blank(r[^1]) : null))
            .ToList();

    /// <summary>The rows of every table in the output, each split into its columns' trimmed text.</summary>
    private static IEnumerable<string[]> Rows(string output)
    {
        // A progress spinner rewrites its line with carriage returns; only the text after the last one shows.
        var lines = Ansi().Replace(output, "").Split('\n').Select(l => l.TrimEnd('\r').Split('\r')[^1]).ToList();
        for (var i = 1; i < lines.Count; i++)
        {
            if (!Dashes().IsMatch(lines[i]))
                continue;
            var starts = ColumnStarts(lines[i - 1]);
            if (starts.Count < 3)
                continue;
            for (i++; i < lines.Count && lines[i].Trim().Length > 0; i++)
            {
                var cells = Cells(lines[i], starts);
                if (cells[1].Length > 0 && !cells[1].EndsWith('…'))
                    yield return cells;
            }
        }
    }

    /// <summary>Where each header word starts, in display cells.</summary>
    private static List<int> ColumnStarts(string header)
    {
        var starts = new List<int>();
        var column = 0;
        var previous = ' ';
        foreach (var c in header)
        {
            if (c != ' ' && previous == ' ')
                starts.Add(column);
            column += Width(c);
            previous = c;
        }
        return starts;
    }

    private static string[] Cells(string row, List<int> starts)
    {
        var cells = new System.Text.StringBuilder[starts.Count];
        for (var k = 0; k < cells.Length; k++)
            cells[k] = new();
        var column = 0;
        var index = 0;
        foreach (var c in row)
        {
            while (index + 1 < starts.Count && column >= starts[index + 1])
                index++;
            cells[index].Append(c);
            column += Width(c);
        }
        return cells.Select(b => b.ToString().Trim()).ToArray();
    }

    /// <summary>East Asian wide characters take two console cells, and winget pads by cells.</summary>
    private static int Width(char c) => c switch
    {
        >= 'ᄀ' and <= 'ᅟ' or >= '⺀' and <= '꓏' or >= '가' and <= '힣'
            or >= '豈' and <= '﫿' or >= '︰' and <= '﹏' or >= '＀' and <= '｠'
            or >= '￠' and <= '￦' => 2,
        _ => 1,
    };

    private static string? Blank(string value) => value.Length > 0 ? value : null;

    [GeneratedRegex(@"^\s*-{10,}\s*$")]
    private static partial Regex Dashes();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex Ansi();
}
