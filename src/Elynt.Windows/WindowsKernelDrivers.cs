using System.Diagnostics;
using Elynt.Core.Drivers;
using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>
/// The kernel drivers Windows is set to load: services of type 1 (kernel driver) that aren't disabled, named by their
/// .sys file, with the file's version. Read-only.
/// </summary>
public sealed class WindowsKernelDrivers : IKernelDrivers
{
    private const int KernelDriver = 1;
    private const int Disabled = 4;

    public IReadOnlyList<KernelDriver> Installed()
    {
        if (!OperatingSystem.IsWindows())
            return [];

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services")
            ?? throw new InvalidOperationException("Couldn't open the services key.");
        var drivers = new List<KernelDriver>();
        foreach (var name in services.GetSubKeyNames())
        {
            using var service = services.OpenSubKey(name);
            if (service?.GetValue("Type") is not int type || type != KernelDriver)
                continue;
            if (service.GetValue("Start") is int start && start == Disabled)
                continue;

            var image = service.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            var path = Resolve(image, name, windows);
            drivers.Add(new KernelDriver(Path.GetFileNameWithoutExtension(path), FileVersion(path)));
        }

        return drivers;
    }

    /// <summary>Turns the forms ImagePath takes (\SystemRoot\…, System32\…, \??\C:\…, %SystemRoot%\…) into a file path.</summary>
    private static string Resolve(string? image, string service, string windows)
    {
        if (string.IsNullOrWhiteSpace(image))
            return Path.Combine(windows, "System32", "drivers", service + ".sys");

        var path = Environment.ExpandEnvironmentVariables(image.Trim().Trim('"'));
        if (path.StartsWith(@"\??\", StringComparison.Ordinal))
            return path[4..];
        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(windows, path[@"\SystemRoot\".Length..]);
        if (!Path.IsPathRooted(path))
            return Path.Combine(windows, path);
        return path;
    }

    private static string? FileVersion(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var info = FileVersionInfo.GetVersionInfo(path);
            return info.FileMajorPart + info.FileMinorPart + info.FileBuildPart + info.FilePrivatePart == 0
                ? null
                : $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
        }
        catch (Exception)
        {
            return null;
        }
    }
}
