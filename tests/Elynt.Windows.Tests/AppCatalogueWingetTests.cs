using System.Diagnostics;
using Elynt.Core.Apps;

namespace Elynt.Windows.Tests;

/// <summary>Every catalogue app id resolves to exactly one package in winget's community source.</summary>
public sealed class AppCatalogueWingetTests
{
    public static TheoryData<string> CatalogueApps() => new(AppCatalogue.Default.Apps.Select(a => a.Id));

    [Theory]
    [MemberData(nameof(CatalogueApps))]
    public void The_app_exists_in_the_community_source(string id)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && WingetOnPath(), "Needs winget.");

        var (exit, output) = Winget("show", "--id", id, "--exact", "--source", "winget", "--accept-source-agreements", "--disable-interactivity");

        Assert.True(exit == 0, $"winget show {id} exited 0x{exit:X8}: {output}");
    }

    private static bool WingetOnPath()
    {
        try
        {
            return Winget("--version").Exit == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static (int Exit, string Output) Winget(params string[] args)
    {
        var start = new ProcessStartInfo("winget") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Result + errors);
    }
}
