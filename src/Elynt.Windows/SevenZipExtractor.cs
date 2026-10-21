using System.Diagnostics;
using Elynt.Core.Drivers;
using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>
/// Unpacks NVIDIA's self-extracting driver package with an installed 7-Zip (7z.exe), run as a child process; no
/// 7-Zip code is linked or shipped. Only machine-wide installs are used (HKLM's 7-Zip path or Program Files), which
/// only administrators can change, because the service runs the result.
/// </summary>
/// <param name="sevenZip">The 7z.exe to use; found automatically when null.</param>
public sealed class SevenZipExtractor(string? sevenZip = null) : IPackageExtractor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public void Extract(string archive, string destination)
    {
        var tool = sevenZip ?? Find()
            ?? throw new InvalidOperationException("Lean install needs 7-Zip installed (free from 7-zip.org).");

        Directory.CreateDirectory(destination);
        var start = new ProcessStartInfo(tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // x: keep folder paths; -y: answer yes to overwrites; -bd: no progress indicator.
        foreach (var argument in new[] { "x", "-y", "-bd", $"-o{destination}", "--", archive })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Couldn't start 7-Zip.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"7-Zip was still unpacking after {Timeout.TotalMinutes:0} minutes and was stopped.");
        }

        // 0 is success and 1 a warning (e.g. a locked file it skipped); 2 and up are errors.
        if (process.ExitCode >= 2)
            throw new InvalidDataException($"7-Zip couldn't unpack the driver (exit code {process.ExitCode}): {(error.Result + output.Result).Trim()}");
    }

    /// <summary>The machine-wide 7z.exe, or null when 7-Zip isn't installed.</summary>
    public static string? Find()
    {
        var candidates = new List<string>();
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\7-Zip");
            foreach (var name in new[] { "Path64", "Path" })
                if (key?.GetValue(name) is string folder && !string.IsNullOrWhiteSpace(folder))
                    candidates.Add(Path.Combine(folder, "7z.exe"));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Fall back to the default install folders.
        }

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }
}
