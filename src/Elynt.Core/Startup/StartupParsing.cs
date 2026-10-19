using System.Xml;
using System.Xml.Linq;

namespace Elynt.Core.Startup;

/// <summary>Finds the program a startup command line runs, for its signature and impact.</summary>
public static class StartupCommandLine
{
    private static readonly string[] ProgramExtensions = [".exe", ".com", ".cmd", ".bat", ".ps1", ".vbs", ".js", ".lnk"];

    /// <summary>
    /// The program path: the quoted first part, or (unquoted, as Windows allows) everything up to the first program
    /// extension. Null when there is none. Environment variables must already be expanded.
    /// </summary>
    public static string? ExecutablePath(string command)
    {
        command = command.Trim();
        if (command.Length == 0)
            return null;
        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }

        foreach (var extension in ProgramExtensions)
        {
            var at = command.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (at > 0 && (at + extension.Length == command.Length || command[at + extension.Length] == ' '))
                return command[..(at + extension.Length)];
        }
        var space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }
}

/// <summary>
/// Reads one task definition as Task Scheduler stores it (System32\Tasks, the Task Scheduler 1.2 schema).
/// </summary>
public static class ScheduledTaskXml
{
    /// <returns>The task, or null when the file can't be read.</returns>
    public static ScheduledTaskInfo? Parse(string taskPath, string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            return null;
        }
        if (document.Root is not { } task)
            return null;

        var settings = Child(task, "Settings");
        var enabled = Child(settings, "Enabled")?.Value is not { } value || !value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
        var signIn = Child(task, "Triggers")?.Elements().Any(e => e.Name.LocalName == "LogonTrigger") == true;
        var exec = Child(task, "Actions")?.Elements().FirstOrDefault(e => e.Name.LocalName == "Exec");
        var program = Child(exec, "Command")?.Value.Trim();
        var arguments = Child(exec, "Arguments")?.Value.Trim();
        var command = program is null ? null : string.IsNullOrEmpty(arguments) ? program : $"{program} {arguments}";

        return new ScheduledTaskInfo(
            taskPath,
            enabled,
            signIn,
            Child(Child(task, "RegistrationInfo"), "Author")?.Value.Trim(),
            command,
            program is null ? null : StartupCommandLine.ExecutablePath(program));
    }

    private static XElement? Child(XElement? parent, string name) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);
}
