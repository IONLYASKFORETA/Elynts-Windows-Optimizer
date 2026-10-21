using Elynt.Core.Boot;

namespace Elynt.Windows;

/// <summary>
/// The current boot entry through bcdedit. Needs admin rights. Element names in bcdedit's output are read as English
/// names (e.g. "useplatformclock"); this has not been checked on a non-English Windows.
/// </summary>
public sealed class WindowsBootConfig : IBootConfig
{
    private const string Current = "{current}";

    public string? Get(string element)
    {
        foreach (var line in CommandLine.Run("bcdedit.exe", "/enum", Current).Split('\n'))
        {
            var trimmed = line.Trim();
            var space = trimmed.IndexOfAny([' ', '\t']);
            if (space > 0 && trimmed[..space].Equals(element, StringComparison.OrdinalIgnoreCase))
                return trimmed[space..].Trim();
        }
        return null;
    }

    public void Set(string element, string value) => CommandLine.Run("bcdedit.exe", "/set", Current, element, value);

    public void Delete(string element)
    {
        // bcdedit fails when the element isn't set; deleting an absent setting is a no-op here.
        if (Get(element) is not null)
            CommandLine.Run("bcdedit.exe", "/deletevalue", Current, element);
    }
}
