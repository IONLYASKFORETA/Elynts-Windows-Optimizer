using Elynt.Core.Apps;

namespace Elynt.Windows.Tests;

/// <summary>
/// Reading winget's console tables, used when the WinGet PowerShell module can't run (the system account with
/// nobody signed in). The samples are winget's own output shape: a progress spinner, a header row, a line of
/// dashes, then fixed-width rows.
/// </summary>
public sealed class WingetTableTests
{
    private const string SearchOutput =
        "   - \r   \\ \r   | \r                                                                                                                        \r" +
        "Name                     Id                          Version     Match        Source\r\n" +
        "--------------------------------------------------------------------------------------\r\n" +
        "Steam                    Valve.Steam                 2.10.91.91               winget\r\n" +
        "Steam ROM Manager        SteamGridDB.SteamROMManager 2.5.29      Tag: steam   winget\r\n" +
        "SteamCMD for Windows lo… Valve.SteamCMD.WithLongerI… 1.0         Tag: steam   winget\r\n";

    private const string ListOutput =
        "   - \r   \\ \r" +
        "Name                                   Id                                     Version         Available      Source\n" +
        "---------------------------------------------------------------------------------------------------------------------\n" +
        "Microsoft Edge                         Microsoft.Edge                         141.0.3537.71                  winget\n" +
        "Microsoft Visual C++ 2015-2022 Redist… Microsoft.VCRedist.2015+.x64           14.44.35211.0   14.50.35719.0  winget\n" +
        "7-Zip 25.01 (x64)                      7zip.7zip                              25.01\n" +
        "Steam                                  Valve.Steam                            2.10.91.91                     winget\n" +
        "\n" +
        "1 upgrade available.\n";

    [Fact]
    public void Search_rows_are_read_by_column_and_rows_with_a_cut_off_id_are_dropped()
    {
        var results = WingetTable.ParseSearch(SearchOutput);

        Assert.Equal(
        [
            new PackageInfo("Valve.Steam", "Steam", "2.10.91.91"),
            new PackageInfo("SteamGridDB.SteamROMManager", "Steam ROM Manager", "2.5.29"),
        ], results);
    }

    [Fact]
    public void Installed_rows_carry_the_available_version_and_source_when_winget_shows_them()
    {
        var installed = WingetTable.ParseList(ListOutput);

        Assert.Equal(
        [
            new InstalledPackage("Microsoft.Edge", "Microsoft Edge", "141.0.3537.71", null, "winget"),
            new InstalledPackage("Microsoft.VCRedist.2015+.x64", "Microsoft Visual C++ 2015-2022 Redist…", "14.44.35211.0", "14.50.35719.0", "winget"),
            new InstalledPackage("7zip.7zip", "7-Zip 25.01 (x64)", "25.01", null, null),
            new InstalledPackage("Valve.Steam", "Steam", "2.10.91.91", null, "winget"),
        ], installed);
    }

    [Fact]
    public void No_table_means_no_packages()
    {
        Assert.Empty(WingetTable.ParseSearch("   - \rNo package found matching input criteria.\r\n"));
        Assert.Empty(WingetTable.ParseList(""));
    }
}
