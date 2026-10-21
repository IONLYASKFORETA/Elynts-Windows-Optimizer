using System.Diagnostics;
using Elynt.Windows;

namespace Elynt.Windows.Tests;

/// <summary>The real 7-Zip unpacker, on a Windows PC with 7-Zip installed (GitHub's Windows runners have it). Skipped elsewhere.</summary>
public sealed class SevenZipExtractorTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("elynt-7z-").FullName;

    public SevenZipExtractorTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs real Windows.");
        Assert.SkipUnless(SevenZipExtractor.Find() is not null, "7-Zip isn't installed.");
    }

    public void Dispose() => Directory.Delete(_work, recursive: true);

    [Fact]
    public void Unpacks_an_archive_keeping_its_folders()
    {
        var source = Directory.CreateDirectory(Path.Combine(_work, "source")).FullName;
        Directory.CreateDirectory(Path.Combine(source, "Display.Driver"));
        File.WriteAllText(Path.Combine(source, "setup.cfg"), "<setup/>");
        File.WriteAllText(Path.Combine(source, "Display.Driver", "DisplayDriver.nvi"), "<nvi name=\"Display.Driver\"/>");
        var archive = Path.Combine(_work, "package.7z");
        using (var pack = Process.Start(new ProcessStartInfo(SevenZipExtractor.Find()!, ["a", "-bd", archive, Path.Combine(source, "*")])
               { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
        {
            pack.StandardOutput.ReadToEnd();
            pack.WaitForExit();
            Assert.Equal(0, pack.ExitCode);
        }

        var destination = Path.Combine(_work, "out");
        new SevenZipExtractor().Extract(archive, destination);

        Assert.Equal("<setup/>", File.ReadAllText(Path.Combine(destination, "setup.cfg")));
        Assert.True(File.Exists(Path.Combine(destination, "Display.Driver", "DisplayDriver.nvi")));
    }

    [Fact]
    public void A_file_that_isnt_an_archive_throws()
    {
        var bogus = Path.Combine(_work, "bogus.exe");
        File.WriteAllText(bogus, "not an archive");

        Assert.ThrowsAny<Exception>(() => new SevenZipExtractor().Extract(bogus, Path.Combine(_work, "out")));
    }
}
