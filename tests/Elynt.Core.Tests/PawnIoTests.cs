using Elynt.Core.Games;
using Elynt.Core.Journal;
using Elynt.Core.LowLevel;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>The optional PawnIO driver: installed only when the user turns it on, with a FACEIT warning, and removable.</summary>
public sealed class PawnIoTests : IDisposable
{
    private readonly FakePawnIo _pawnIo = new();
    private readonly FakeDownloader _downloader = new() { Content = [0x4D, 0x5A, 1, 2, 3] };
    private readonly FakeSignatureVerifier _signatures = new() { Result = new(Trusted: true, Signer: "namazso") };
    private readonly FakeGameSources _pc = new();
    private readonly InMemoryGameStore _games = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "elynt-pawnio-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private Engine NewEngine() => new(
        new Catalogue([LabelTests.TestTweak("test.plain")]),
        new SystemAdapters(new FakeRegistry(), new FakeSystemInfo(), Games: _pc)
        {
            PawnIo = new PawnIoAdapters(_pawnIo, _pawnIo, _folder, _downloader, _signatures),
        },
        new ChangeJournal(new InMemoryJournalStore()),
        "S-1-5-21-test",
        gameStore: _games);

    private Engine WithCs2()
    {
        _pc.File(@"C:\Games\CS2\game\bin\win64\cs2.exe");
        var engine = NewEngine();
        Assert.True(engine.AddGame(@"C:\Games\CS2\game\bin\win64\cs2.exe").Succeeded);
        return engine;
    }

    [Fact]
    public void Pawnio_is_not_installed_until_the_user_turns_it_on()
    {
        var engine = NewEngine();

        var report = engine.GetPawnIo();
        engine.GetSensors();
        engine.GetBiosReport();

        Assert.False(report.Installed);
        Assert.Empty(_pawnIo.Installs);
        Assert.Null(_downloader.LastUrl);
    }

    [Fact]
    public void Installing_without_accepting_the_faceit_warning_is_refused()
    {
        var result = NewEngine().InstallPawnIo(acceptFaceitWarning: false);

        Assert.False(result.Succeeded);
        Assert.Contains("FACEIT", result.Message);
        Assert.Empty(_pawnIo.Installs);
        Assert.Null(_downloader.LastUrl);
    }

    [Fact]
    public void Accepting_the_warning_downloads_checks_and_installs_pawnio_and_names_faceit_games()
    {
        var result = WithCs2().InstallPawnIo(acceptFaceitWarning: true);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.Report.Installed);
        Assert.Equal("2.0.1", result.Report.Version);
        Assert.Equal([_downloader.Content], _pawnIo.Installs);
        Assert.Equal(Uri.UriSchemeHttps, _downloader.LastUrl!.Scheme);
        Assert.Contains("cs2", result.Message);
        Assert.Contains("FACEIT", result.Message);
        Assert.Empty(Directory.GetFiles(_folder, "*.exe"));
    }

    [Fact]
    public void The_faceit_warning_names_installed_faceit_games_before_anything_is_installed()
    {
        var report = WithCs2().GetPawnIo();

        Assert.Equal(["cs2"], report.FaceitGames);
        Assert.Equal(
            "FACEIT's anti-cheat blocks PawnIO, so while it is installed FACEIT may refuse to start. This affects cs2 on FACEIT. Uninstall the driver before playing on FACEIT.",
            report.FaceitWarning);
    }

    [Fact]
    public void A_setup_that_is_not_validly_signed_is_deleted_and_never_run()
    {
        _signatures.Result = new(Trusted: false, Signer: null);

        var result = NewEngine().InstallPawnIo(acceptFaceitWarning: true);

        Assert.False(result.Succeeded);
        Assert.Contains("isn't validly signed", result.Message);
        Assert.Empty(_pawnIo.Installs);
        Assert.False(result.Report.Installed);
        Assert.Empty(Directory.GetFiles(_folder, "*.exe"));
    }

    [Fact]
    public void A_failing_setup_reports_pawnio_as_not_installed()
    {
        _pawnIo.InstallExitCode = 5;

        var result = NewEngine().InstallPawnIo(acceptFaceitWarning: true);

        Assert.False(result.Succeeded);
        Assert.Contains("exit code 5", result.Message);
        Assert.False(result.Report.Installed);
    }

    [Fact]
    public void Uninstall_driver_removes_pawnio_and_its_modules()
    {
        var engine = NewEngine();
        Assert.True(engine.InstallPawnIo(acceptFaceitWarning: true).Succeeded);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_folder, "modules")));

        var result = engine.UninstallPawnIo();

        Assert.True(result.Succeeded, result.Message);
        Assert.False(result.Report.Installed);
        Assert.False(engine.GetPawnIo().Installed);
        Assert.Null(_pawnIo.Version);
        Assert.False(Directory.Exists(Path.Combine(_folder, "modules")));
    }

    [Fact]
    public void A_failing_uninstaller_says_pawnio_is_still_installed()
    {
        _pawnIo.Version = "2.0.1";
        _pawnIo.UninstallExitCode = 1603;

        var result = NewEngine().UninstallPawnIo();

        Assert.False(result.Succeeded);
        Assert.Contains("still installed", result.Message);
        Assert.True(result.Report.Installed);
    }

    [Fact]
    public void Installing_when_already_installed_runs_nothing()
    {
        _pawnIo.Version = "2.0.1";

        var result = NewEngine().InstallPawnIo(acceptFaceitWarning: true);

        Assert.True(result.Succeeded);
        Assert.Empty(_pawnIo.Installs);
    }
}
