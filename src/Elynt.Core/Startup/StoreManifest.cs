using System.Xml;
using System.Xml.Linq;

namespace Elynt.Core.Startup;

/// <summary>
/// Reads the startup tasks a Store (MSIX) app declares in its AppxManifest.xml: a <c>windows.startupTask</c>
/// extension holding a <c>StartupTask</c> (uap5 or desktop namespace) with TaskId, Enabled and DisplayName.
/// </summary>
public static class StoreManifest
{
    public static IReadOnlyList<StoreStartupTask> StartupTasks(string manifestXml, string packageFamilyName, string installFolder)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(manifestXml);
        }
        catch (XmlException)
        {
            return [];
        }

        var properties = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Properties");
        var packageName = Readable(Child(properties, "DisplayName")) ?? packageFamilyName.Split('_')[0];
        var publisher = Readable(Child(properties, "PublisherDisplayName"));

        var tasks = new List<StoreStartupTask>();
        foreach (var extension in document.Descendants().Where(e =>
                     e.Name.LocalName == "Extension" && (string?)e.Attribute("Category") == "windows.startupTask"))
        {
            foreach (var task in extension.Elements().Where(e => e.Name.LocalName == "StartupTask"))
            {
                var taskId = (string?)task.Attribute("TaskId");
                if (string.IsNullOrEmpty(taskId))
                    continue;
                var executable = (string?)extension.Attribute("Executable") ??
                                 (string?)extension.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Application")?.Attribute("Executable");
                tasks.Add(new StoreStartupTask(
                    packageFamilyName,
                    taskId,
                    Readable((string?)task.Attribute("DisplayName")) ?? packageName,
                    publisher,
                    EnabledByDefault: string.Equals((string?)task.Attribute("Enabled"), "true", StringComparison.OrdinalIgnoreCase),
                    executable is null ? null : $@"{installFolder.TrimEnd('\\')}\{executable.Replace('/', '\\')}"));
            }
        }
        return tasks;
    }

    private static string? Child(XElement? parent, string name) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    /// <summary>Names that point into the app's resources (ms-resource:) can't be shown as they are.</summary>
    private static string? Readable(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? null : name;
}
