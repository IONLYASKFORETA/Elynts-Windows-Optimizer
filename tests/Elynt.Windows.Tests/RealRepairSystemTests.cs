using System.Diagnostics;
using Elynt.Core.Repair;

namespace Elynt.Windows.Tests;

/// <summary>The real repair adapter's program runner and cache clearing, against a scratch folder in this user's profile.</summary>
public sealed class RealRepairSystemTests : IDisposable
{
    private static readonly string CurrentUserSid =
        OperatingSystem.IsWindows() ? System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value : "";

    private readonly string _scratch = $@"ElyntOptimizerTests\{Guid.NewGuid():N}";
    private readonly WindowsRepairSystem _pc = new(CurrentUserSid);

    public RealRepairSystemTests() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs real Windows.");

    private string Full(string relative) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), _scratch, relative);

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ElyntOptimizerTests");
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task A_program_streams_each_line_and_returns_its_exit_code()
    {
        var lines = new List<string>();

        var exitCode = await _pc.RunAsync(
            new ProgramRun("cmd.exe", ["/c", "echo one& echo 50% done& exit 3"]), lines.Add, TestContext.Current.CancellationToken);

        Assert.Equal(3, exitCode);
        Assert.Equal(["one", "50% done"], lines);
    }

    [Fact]
    public async Task A_program_is_only_run_from_System32()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _pc.RunAsync(new ProgramRun(@"..\cmd.exe", []), _ => { }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _pc.RunAsync(new ProgramRun("no-such-program.exe", []), _ => { }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Clearing_deletes_matching_files_and_counts_files_in_use()
    {
        Directory.CreateDirectory(Full(@"cache\sub"));
        File.WriteAllText(Full(@"cache\iconcache_32.db"), "x");
        File.WriteAllText(Full(@"cache\iconcache_48.db"), "x");
        File.WriteAllText(Full(@"cache\thumbcache_32.db"), "x");
        File.WriteAllText(Full(@"cache\sub\iconcache_16.db"), "x");
        using var locked = new FileStream(Full(@"cache\iconcache_48.db"), FileMode.Open, FileAccess.Read, FileShare.None);

        var result = _pc.ClearFolder(RepairFolder.UserLocalAppData, $@"{_scratch}\cache", "iconcache_*.db", includeSubfolders: false, CurrentUserSid);

        Assert.Equal(new FolderClearResult(1, 1), result);
        Assert.True(File.Exists(Full(@"cache\thumbcache_32.db")));
        Assert.True(File.Exists(Full(@"cache\sub\iconcache_16.db")));
        Assert.True(_pc.FolderExists(RepairFolder.UserLocalAppData, $@"{_scratch}\cache", CurrentUserSid));
    }

    [Fact]
    public void Clearing_never_follows_a_junction_out_of_the_folder()
    {
        Directory.CreateDirectory(Full("outside"));
        File.WriteAllText(Full(@"outside\precious.txt"), "keep me");
        Directory.CreateDirectory(Full("cache"));
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", Full(@"cache\link"), Full("outside")]) { CreateNoWindow = true }))
            mklink!.WaitForExit();

        _pc.ClearFolder(RepairFolder.UserLocalAppData, $@"{_scratch}\cache", "*", includeSubfolders: true, CurrentUserSid);
        Assert.Throws<IOException>(() =>
            _pc.ClearFolder(RepairFolder.UserLocalAppData, $@"{_scratch}\cache\link", "*", includeSubfolders: true, CurrentUserSid));

        Assert.True(File.Exists(Full(@"outside\precious.txt")));
    }

    [Fact]
    public void A_missing_folder_clears_nothing()
    {
        Assert.Equal(new FolderClearResult(0, 0),
            _pc.ClearFolder(RepairFolder.UserLocalAppData, $@"{_scratch}\nope", "*", includeSubfolders: true, CurrentUserSid));
        Assert.False(_pc.FolderExists(RepairFolder.UserLocalAppData, $@"{_scratch}\nope", CurrentUserSid));
    }
}
