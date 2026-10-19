using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Elynt.Core.Startup;

/// <summary>
/// Reads the files the Diagnostic Policy Service writes after each sign-in, which Task Manager's impact column is
/// built from: <c>%SystemRoot%\System32\wdi\LogFiles\StartupInfo\&lt;SID&gt;_StartupInfo&lt;n&gt;.xml</c>. Each
/// <c>Process</c> element names the program and carries <c>CpuUsage</c> (milliseconds) and <c>DiskUsage</c> (bytes).
/// The format is undocumented, so anything unreadable is skipped rather than guessed at.
/// </summary>
public static class StartupInfoFile
{
    public static IReadOnlyList<StartupMeasurement> Parse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            return [];
        }

        var measurements = new List<StartupMeasurement>();
        foreach (var process in document.Descendants().Where(e => e.Name.LocalName == "Process"))
        {
            var name = process.Attribute("Name")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                !long.TryParse(Child(process, "CpuUsage"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cpu) ||
                !long.TryParse(Child(process, "DiskUsage"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var disk))
                continue;
            measurements.Add(new StartupMeasurement(name, TimeSpan.FromMilliseconds(cpu), disk));
        }
        return measurements;
    }

    private static string? Child(XElement element, string name) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
}
