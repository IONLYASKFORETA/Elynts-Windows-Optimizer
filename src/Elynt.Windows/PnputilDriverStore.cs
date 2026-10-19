using System.Text.RegularExpressions;
using Elynt.Core.Drivers;

namespace Elynt.Windows;

/// <summary>
/// Windows' driver store through pnputil, run by full path from System32. pnputil's labels are localised, so packages
/// are read by the shape and order of the values rather than by label.
/// </summary>
public sealed partial class PnputilDriverStore : IDriverStore
{
    /// <summary>pnputil's "done, restart needed" (ERROR_SUCCESS_REBOOT_REQUIRED).</summary>
    private const int RebootRequired = 3010;

    public IReadOnlyList<DriverPackage> Packages() => Parse(Run("/enum-drivers"));

    public void Export(string publishedName, string folder)
    {
        Directory.CreateDirectory(folder);
        Run("/export-driver", Checked(publishedName), folder);
    }

    public void Delete(string publishedName) => Run("/delete-driver", Checked(publishedName), "/uninstall", "/force");

    public void Restore(string folder) => Run("/add-driver", Path.Combine(folder, "*.inf"), "/subdirs", "/install");

    /// <summary>Reads <c>pnputil /enum-drivers</c> output in any display language.</summary>
    public static IReadOnlyList<DriverPackage> Parse(string output)
    {
        var packages = new List<DriverPackage>();
        foreach (var block in Regex.Split(output.Replace("\r\n", "\n"), @"\n\s*\n"))
        {
            var values = block.Split('\n')
                .Select(line => line.IndexOf(':') is var colon and >= 0 ? line[(colon + 1)..].Trim() : null)
                .OfType<string>()
                .ToList();
            var published = values.FindIndex(v => PublishedName().IsMatch(v));
            if (published < 0 || published + 2 >= values.Count)
                continue;

            var guid = values.FirstOrDefault(v => ClassGuid().IsMatch(v)) ?? "";
            var version = values.Select(v => DriverVersion().Match(v)).LastOrDefault(m => m.Success)?.Groups[1].Value ?? "";
            packages.Add(new DriverPackage(values[published], values[published + 1], values[published + 2], guid.ToLowerInvariant(), version));
        }

        return packages;
    }

    /// <summary>Only ever hands pnputil a store name like oem12.inf, never a path or a switch.</summary>
    private static string Checked(string publishedName) =>
        PublishedName().IsMatch(publishedName) ? publishedName : throw new ArgumentException($"Not a driver store name: {publishedName}");

    private static string Run(params string[] arguments) =>
        SystemCommand.Run("pnputil.exe", arguments, TimeSpan.FromMinutes(10), [RebootRequired]);

    [GeneratedRegex(@"^oem\d+\.inf$", RegexOptions.IgnoreCase)]
    private static partial Regex PublishedName();

    [GeneratedRegex(@"^\{[0-9a-fA-F-]{36}\}$")]
    private static partial Regex ClassGuid();

    [GeneratedRegex(@"(?:^|\s)(\d+\.\d+\.\d+\.\d+)$")]
    private static partial Regex DriverVersion();
}
