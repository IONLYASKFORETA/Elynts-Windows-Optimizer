using System.Windows;

namespace Elynt.App;

/// <summary>The .exe pickers: a game for its profile, or an app to close while playing.</summary>
internal static class FilePickers
{
    /// <param name="folder">The game's folder to open in, when known.</param>
    public static string? Game(Window? owner, string? folder = null) => Exe(owner, "Choose the game's .exe", folder);

    public static string? AppToClose(Window? owner) => Exe(owner, "Choose the app to close while playing");

    private static string? Exe(Window? owner, string title, string? folder = null)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = "Programs (*.exe)|*.exe",
            CheckFileExists = true,
            InitialDirectory = folder ?? "",
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
}
