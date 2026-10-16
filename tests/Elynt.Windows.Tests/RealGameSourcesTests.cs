using System.Text;
using Elynt.Core.Games;

namespace Elynt.Windows.Tests;

/// <summary>The real file and HKLM reads behind game detection, on a real Windows PC. Skipped elsewhere.</summary>
public sealed class RealGameSourcesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"elynt-games-{Guid.NewGuid():N}");

    public RealGameSourcesTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs the real Windows file system and registry.");
        Directory.CreateDirectory(Path.Combine(_folder, "Game", "BattlEye"));
        File.WriteAllText(Path.Combine(_folder, "Game", "Game.exe"), "");
        File.WriteAllText(Path.Combine(_folder, "manifest.item"), "{ \"DisplayName\": \"Ünïcode\" }", Encoding.UTF8);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Reads_files_folders_and_HKLM_values()
    {
        IGameSources pc = new WindowsGameSources();

        Assert.True(pc.FileExists(Path.Combine(_folder, "Game", "Game.exe")));
        Assert.True(pc.DirectoryExists(Path.Combine(_folder, "Game", "BattlEye")));
        Assert.Equal([Path.Combine(_folder, "manifest.item")], pc.Files(_folder, ".item"));
        Assert.Equal([Path.Combine(_folder, "Game")], pc.Directories(_folder));
        Assert.Contains("Ünïcode", pc.ReadText(Path.Combine(_folder, "manifest.item")));
        Assert.Null(pc.ReadText(Path.Combine(_folder, "missing.txt")));
        Assert.NotEmpty(pc.Drives);
        Assert.NotEmpty(pc.SubKeys(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"));
        Assert.NotNull(pc.Value(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild"));
        Assert.Empty(pc.SubKeys(@"SOFTWARE\Elynt\DoesNotExist"));
    }
}
