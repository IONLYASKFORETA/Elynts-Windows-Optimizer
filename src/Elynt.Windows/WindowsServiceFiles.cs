using System.Diagnostics;
using Elynt.Core.Setup;

namespace Elynt.Windows;

/// <summary>Service build folders on disk. A build is identified by its executable's product version.</summary>
public sealed class WindowsServiceFiles : IServiceFiles
{
    public string? VersionAt(string folder)
    {
        var exe = Path.Combine(folder, ServiceSetup.ExeName);
        return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "" : null;
    }

    public void Mirror(string from, string to) =>
        // A stopped service's process can take a moment to exit and release its files.
        Retry(() =>
        {
            Directory.CreateDirectory(to);
            CopyTree(new DirectoryInfo(from), new DirectoryInfo(to));
        });

    public void DeleteFolder(string folder) =>
        Retry(() =>
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        });

    private static void CopyTree(DirectoryInfo from, DirectoryInfo to)
    {
        var wantedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in from.GetFiles())
        {
            wantedFiles.Add(file.Name);
            file.CopyTo(Path.Combine(to.FullName, file.Name), overwrite: true);
        }

        var wantedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in from.GetDirectories())
        {
            wantedDirs.Add(dir.Name);
            CopyTree(dir, to.CreateSubdirectory(dir.Name));
        }

        // An exact copy: files the new build no longer has are removed.
        foreach (var stale in to.GetFiles().Where(f => !wantedFiles.Contains(f.Name)))
            stale.Delete();
        foreach (var stale in to.GetDirectories().Where(d => !wantedDirs.Contains(d.Name)))
            stale.Delete(recursive: true);
    }

    private static void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < 10 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
    }
}
