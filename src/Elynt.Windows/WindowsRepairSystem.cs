using System.Diagnostics;
using System.Globalization;
using System.Text;
using Elynt.Core.Repair;
using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>Runs repair tools on the real PC, as the service (SYSTEM), on behalf of one signed-in user.</summary>
public sealed class WindowsRepairSystem(string userSid) : IRepairSystem
{
    static WindowsRepairSystem() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public string SystemDrive { get; } = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";

    public async Task<int> RunAsync(ProgramRun run, Action<string> output, CancellationToken cancellation)
    {
        var encoding = run.Output == ConsoleText.Unicode
            ? Encoding.Unicode
            : Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var start = new ProcessStartInfo(ResolveProgram(run.Program))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
        };
        foreach (var argument in run.Arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Couldn't start {run.Program}.");
        var gate = new object();
        void Emit(string line)
        {
            lock (gate)
                output(line);
        }
        var readers = Task.WhenAll(PumpLinesAsync(process.StandardOutput, Emit), PumpLinesAsync(process.StandardError, Emit));
        try
        {
            await process.WaitForExitAsync(cancellation);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        await readers;
        return process.ExitCode;
    }

    public bool FolderExists(RepairFolder root, string relativePath, string user) =>
        Directory.Exists(Resolve(root, relativePath));

    public FolderClearResult ClearFolder(RepairFolder root, string relativePath, string pattern, bool includeSubfolders, string user)
    {
        var folder = Resolve(root, relativePath);
        if (!Directory.Exists(folder))
            return new(0, 0);

        // Never follow junctions or symlinks: the user can create them in their own profile, and the service must
        // not be steered into deleting files elsewhere.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = includeSubfolders,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
        };
        int removed = 0, inUse = 0;
        foreach (var file in Directory.EnumerateFiles(folder, pattern, options))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                inUse++;
            }
        }

        if (includeSubfolders)
        {
            foreach (var sub in Directory.EnumerateDirectories(folder, "*", options).OrderByDescending(d => d.Length))
            {
                try
                {
                    Directory.Delete(sub);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Not empty (a file was in use) or locked: Windows rebuilds what it needs either way.
                }
            }
        }
        return new(removed, inUse);
    }

    private string Resolve(RepairFolder root, string relativePath)
    {
        if (relativePath.Split('\\', '/').Any(part => part == ".."))
            throw new ArgumentException("Repair paths must stay inside their folder.", nameof(relativePath));

        var basePath = root switch
        {
            RepairFolder.Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            RepairFolder.UserLocalAppData => Path.Combine(UserProfile(), "AppData", "Local"),
            _ => throw new ArgumentOutOfRangeException(nameof(root)),
        };
        var full = relativePath.Length == 0 ? basePath : Path.Combine(basePath, relativePath);

        // Refuse a path that passes through a junction or symlink below the base folder.
        for (var dir = new DirectoryInfo(full); dir is not null && dir.FullName.Length > basePath.Length; dir = dir.Parent)
        {
            if (dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException($"{dir.FullName} is a link to another folder, so it was left alone.");
        }
        return full;
    }

    private string UserProfile()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{userSid}");
        return key?.GetValue("ProfileImagePath") as string
            ?? throw new InvalidOperationException("Couldn't find your user profile folder.");
    }

    /// <summary>Windows programs by bare name from System32 only, never from PATH; winget from its Store package.</summary>
    private static string ResolveProgram(string program)
    {
        if (program.Contains('\\') || program.Contains('/'))
            throw new ArgumentException("Repair programs are named without a folder.", nameof(program));

        var path = program.Equals("winget.exe", StringComparison.OrdinalIgnoreCase)
            ? FindWinget()
            : Path.Combine(Environment.SystemDirectory, program);
        return File.Exists(path) ? path : throw new FileNotFoundException($"{program} isn't on this PC.", path);
    }

    /// <summary>The service runs as SYSTEM, which has no winget alias on PATH, so use App Installer's own folder.</summary>
    private static string FindWinget()
    {
        var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        var newest = Directory.Exists(apps)
            ? Directory.EnumerateDirectories(apps, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe")
                .Select(dir => (dir, version: Version.TryParse(Path.GetFileName(dir).Split('_')[1], out var v) ? v : new Version()))
                .OrderByDescending(x => x.version)
                .Select(x => Path.Combine(x.dir, "winget.exe"))
                .FirstOrDefault(File.Exists)
            : null;
        return newest ?? throw new FileNotFoundException("winget (App Installer) isn't installed. Install it from the Apps section first.");
    }

    /// <summary>Reads a stream character by character so progress lines ending in CR (DISM, SFC) arrive as they're printed.</summary>
    private static async Task PumpLinesAsync(StreamReader reader, Action<string> output)
    {
        var buffer = new char[1024];
        var line = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var c = buffer[i];
                if (c is '\r' or '\n')
                {
                    Flush();
                }
                else if (c != '\0')
                {
                    line.Append(c);
                }
            }
        }
        Flush();

        void Flush()
        {
            var text = line.ToString().Trim();
            line.Clear();
            if (text.Length > 0)
                output(text);
        }
    }
}
