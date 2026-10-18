using Elynt.Core;
using Elynt.Core.Cleanup;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Windows.Tests;

/// <summary>The real cleanup adapter against scratch folders standing in for the cleanup roots.</summary>
public sealed class RealCleanupSystemTests : IDisposable
{
    private static readonly string CurrentUserSid =
        OperatingSystem.IsWindows() ? System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value : "";

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "ElyntCleanupTests", Guid.NewGuid().ToString("N"));
    private readonly WindowsCleanupSystem _pc;

    public RealCleanupSystemTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs real Windows.");
        _pc = new WindowsCleanupSystem(CurrentUserSid, root => Path.Combine(_scratch, root.ToString()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
            Directory.Delete(_scratch, recursive: true);
    }

    private string File(CleanupRoot root, string relative, int bytes)
    {
        var path = Path.Combine(_scratch, root.ToString(), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public void Wildcard_segments_expand_to_every_matching_folder()
    {
        File(CleanupRoot.UserLocalAppData, @"Google\Chrome\User Data\Default\Cache\a", 10);
        File(CleanupRoot.UserLocalAppData, @"Google\Chrome\User Data\Profile 1\Cache\b", 10);
        File(CleanupRoot.UserLocalAppData, @"Google\Chrome\User Data\Profile 2\History", 10);

        var found = _pc.FindFolders(CleanupRoot.UserLocalAppData, @"Google\Chrome\User Data\*\Cache", CurrentUserSid);

        Assert.Equal(["Default", "Profile 1"], found.Select(f => Path.GetFileName(Path.GetDirectoryName(f))).Order());
    }

    [Fact]
    public void Paths_that_climb_out_of_their_root_are_refused() =>
        Assert.Throws<ArgumentException>(() => _pc.FindFolders(CleanupRoot.Windows, @"Temp\..\..", CurrentUserSid));

    [Fact]
    public void A_junction_is_never_followed()
    {
        var outside = File(CleanupRoot.ProgramData, @"Precious\keep.txt", 10);
        Directory.CreateDirectory(Path.Combine(_scratch, "Windows"));
        var link = Path.Combine(_scratch, "Windows", "Temp");
        Directory.CreateSymbolicLink(link, Path.GetDirectoryName(outside)!);

        Assert.Empty(_pc.FindFolders(CleanupRoot.Windows, "Temp", CurrentUserSid));
        _pc.Clear(link, "*", includeSubfolders: true, removeFolder: false);
        Assert.True(System.IO.File.Exists(outside));
    }

    [Fact]
    public async Task Measure_then_clear_a_real_folder_through_the_engine()
    {
        var a = File(CleanupRoot.Windows, @"Temp\a.tmp", 1_000);
        var b = File(CleanupRoot.Windows, @"Temp\sub\b.tmp", 2_000);
        var engine = new Engine(Catalogue.Default, new SystemAdapters(new WindowsRegistry(CurrentUserSid), new WindowsSystemInfo(), Cleanup: _pc),
            new ChangeJournal(new InMemoryJournalStore()), CurrentUserSid);

        // The scratch roots aren't under C:\Users, so the never-touch rules don't hide them.
        var scan = await engine.ScanCleanupAsync(cancellation: TestContext.Current.CancellationToken);
        Assert.Equal(3_000, scan.Categories.Single(c => c.Id == CleanupCategoryIds.TempFiles).SizeBytes);
        Assert.True(System.IO.File.Exists(a));

        var report = await engine.CleanAsync([CleanupCategoryIds.TempFiles], false, cancellation: TestContext.Current.CancellationToken);
        Assert.True(report.Succeeded, report.Message);
        Assert.Equal(3_000, report.BytesFreed);
        Assert.False(System.IO.File.Exists(a));
        Assert.False(System.IO.File.Exists(b));
    }

    [Fact]
    public void A_file_in_use_is_left_and_counted()
    {
        var locked = File(CleanupRoot.Windows, @"Temp\locked.tmp", 10);
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = _pc.Clear(Path.GetDirectoryName(locked)!, "*", includeSubfolders: true, removeFolder: false);

        Assert.Equal(1, result.InUse);
        Assert.True(System.IO.File.Exists(locked));
    }

    [Fact]
    public void Driver_inf_version_section_is_read_with_its_strings()
    {
        var inf = WindowsCleanupSystem.Inf.Parse("""
            [Version]
            Signature = "$Windows NT$"
            Class     = Display ; the class
            Provider  = %NVIDIA%
            DriverVer = 09/18/2025,32.0.15.8129

            [Strings]
            NVIDIA = "NVIDIA"
            """);

        Assert.Equal(new WindowsCleanupSystem.Inf("NVIDIA", "Display", new Version(32, 0, 15, 8129), new DateOnly(2025, 9, 18)), inf);
    }

    [Fact]
    public void Driver_packages_list_without_error_on_a_real_PC()
    {
        var packages = _pc.ListDriverPackages();

        Assert.All(packages, p => Assert.Matches(@"^oem\d+\.inf$", p.PublishedName));
    }
}
