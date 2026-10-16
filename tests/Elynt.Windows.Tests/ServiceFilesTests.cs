using Elynt.Core.Setup;

namespace Elynt.Windows.Tests;

/// <summary>Copying a service build into place on a real disk.</summary>
public sealed class ServiceFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ElyntServiceFilesTests", Guid.NewGuid().ToString("N"));
    private readonly WindowsServiceFiles _files = new();

    public ServiceFilesTests() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows file versions.");

    [Fact]
    public void Mirroring_makes_an_exact_copy_and_drops_files_the_new_build_no_longer_has()
    {
        var from = Directory.CreateDirectory(Path.Combine(_root, "bundled")).FullName;
        var to = Directory.CreateDirectory(Path.Combine(_root, "installed")).FullName;
        File.WriteAllText(Path.Combine(from, "a.dll"), "new");
        Directory.CreateDirectory(Path.Combine(from, "runtimes"));
        File.WriteAllText(Path.Combine(from, "runtimes", "b.dll"), "new");
        File.WriteAllText(Path.Combine(to, "a.dll"), "old");
        File.WriteAllText(Path.Combine(to, "stale.dll"), "old");
        Directory.CreateDirectory(Path.Combine(to, "gone"));

        _files.Mirror(from, to);

        Assert.Equal("new", File.ReadAllText(Path.Combine(to, "a.dll")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(to, "runtimes", "b.dll")));
        Assert.False(File.Exists(Path.Combine(to, "stale.dll")));
        Assert.False(Directory.Exists(Path.Combine(to, "gone")));
    }

    [Fact]
    public void A_folder_without_the_service_executable_has_no_version()
    {
        Directory.CreateDirectory(_root);

        Assert.Null(_files.VersionAt(_root));
        Assert.Null(_files.VersionAt(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void The_version_is_read_from_the_service_executable()
    {
        Directory.CreateDirectory(_root);
        // Any real executable with a product version stands in for Elynt.Service.exe.
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), Path.Combine(_root, ServiceSetup.ExeName));

        Assert.False(string.IsNullOrEmpty(_files.VersionAt(_root)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
