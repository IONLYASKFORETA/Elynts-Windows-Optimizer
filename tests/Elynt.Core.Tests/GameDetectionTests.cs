using Elynt.Core.Games;
using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>Installed games from each launcher, manual adds, and anti-cheat warnings that name them, through the Engine.</summary>
public class GameDetectionTests
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeSystemInfo _system = new();
    private readonly FakeGameSources _pc = new();
    private readonly InMemoryGameStore _store = new();
    private readonly ChangeJournal _journal = new(new InMemoryJournalStore());

    [Fact]
    public void Steam_games_are_found_in_every_library_folder()
    {
        _pc.Key(@"SOFTWARE\WOW6432Node\Valve\Steam", ("InstallPath", @"C:\Program Files (x86)\Steam"))
            .File(@"C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf", """
                "libraryfolders"
                {
                	"0"
                	{
                		"path"		"C:\\Program Files (x86)\\Steam"
                		"apps" { "730" "1" }
                	}
                	"1"
                	{
                		"path"		"D:\\SteamLibrary"
                	}
                }
                """)
            .File(@"C:\Program Files (x86)\Steam\steamapps\appmanifest_730.acf", AppManifest("730", "Counter-Strike 2", "Counter-Strike Global Offensive"))
            .Directory(@"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive")
            .File(@"D:\SteamLibrary\steamapps\appmanifest_359550.acf", AppManifest("359550", "Tom Clancy's Rainbow Six Siege", "Tom Clancy's Rainbow Six Siege"))
            .Directory(@"D:\SteamLibrary\steamapps\common\Tom Clancy's Rainbow Six Siege");

        var games = NewEngine().ListGames();

        Assert.Equal(
            [
                ("Counter-Strike 2", GameLauncher.Steam, @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive"),
                ("Tom Clancy's Rainbow Six Siege", GameLauncher.Steam, @"D:\SteamLibrary\steamapps\common\Tom Clancy's Rainbow Six Siege"),
            ],
            games.Select(g => (g.Name, g.Launcher, g.InstallPath)));
    }

    [Fact]
    public void Epic_games_are_found_from_the_launcher_manifests_and_unfinished_installs_are_skipped()
    {
        const string manifests = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
        _pc.File($@"{manifests}\A1.item", """
                { "DisplayName": "Fortnite", "AppName": "Fortnite", "InstallLocation": "C:\\Program Files\\Epic Games\\Fortnite",
                  "LaunchExecutable": "FortniteGame/Binaries/Win64/FortniteLauncher.exe", "bIsIncompleteInstall": false }
                """)
            .Directory(@"C:\Program Files\Epic Games\Fortnite")
            .File($@"{manifests}\B2.item", """
                { "DisplayName": "Half Downloaded", "AppName": "Half", "InstallLocation": "C:\\Program Files\\Epic Games\\Half",
                  "LaunchExecutable": "Half.exe", "bIsIncompleteInstall": true }
                """)
            .Directory(@"C:\Program Files\Epic Games\Half")
            .File($@"{manifests}\C3.item", "{ not json");

        var game = Assert.Single(NewEngine().ListGames());

        Assert.Equal(
            ("Fortnite", GameLauncher.Epic, @"C:\Program Files\Epic Games\Fortnite", @"C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteLauncher.exe"),
            (game.Name, game.Launcher, game.InstallPath, game.ExePath));
    }

    [Fact]
    public void GOG_games_are_found_from_the_GOG_registry_keys()
    {
        _pc.Key(@"SOFTWARE\WOW6432Node\GOG.com\Games\1207658924",
                ("gameName", "The Witcher 3: Wild Hunt"), ("path", @"D:\GOG Games\The Witcher 3 Wild Hunt"), ("exe", @"D:\GOG Games\The Witcher 3 Wild Hunt\bin\x64\witcher3.exe"))
            .Directory(@"D:\GOG Games\The Witcher 3 Wild Hunt")
            .Key(@"SOFTWARE\WOW6432Node\GOG.com\Games\999", ("gameName", "Uninstalled"), ("path", @"D:\GOG Games\Gone"));

        var game = Assert.Single(NewEngine().ListGames());

        Assert.Equal(
            ("The Witcher 3: Wild Hunt", GameLauncher.Gog, @"D:\GOG Games\The Witcher 3 Wild Hunt", @"D:\GOG Games\The Witcher 3 Wild Hunt\bin\x64\witcher3.exe"),
            (game.Name, game.Launcher, game.InstallPath, game.ExePath));
    }

    [Fact]
    public void Ubisoft_Connect_games_are_found_from_the_launcher_install_keys()
    {
        _pc.Key(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\635", ("InstallDir", "C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Tom Clancy's Rainbow Six Siege/"))
            .Directory(@"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Tom Clancy's Rainbow Six Siege");

        var game = Assert.Single(NewEngine().ListGames());

        Assert.Equal(
            ("Tom Clancy's Rainbow Six Siege", GameLauncher.Ubisoft, @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Tom Clancy's Rainbow Six Siege"),
            (game.Name, game.Launcher, game.InstallPath));
    }

    [Fact]
    public void Battle_net_and_EA_app_games_are_found_from_their_uninstall_entries_but_the_launchers_are_not()
    {
        const string uninstall = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
        _pc.Key($@"{uninstall}\Call of Duty",
                ("DisplayName", "Call of Duty"), ("InstallLocation", @"C:\Program Files (x86)\Call of Duty"),
                ("UninstallString", @"""C:\ProgramData\Battle.net\Agent\Blizzard Uninstaller.exe"" --lang=enUS --uid=auks"))
            .Directory(@"C:\Program Files (x86)\Call of Duty")
            .Key($@"{uninstall}\Battle.net",
                ("DisplayName", "Battle.net"), ("InstallLocation", @"C:\Program Files (x86)\Battle.net"),
                ("UninstallString", @"""C:\ProgramData\Battle.net\Agent\Blizzard Uninstaller.exe"" --uid=battle.net"))
            .Directory(@"C:\Program Files (x86)\Battle.net")
            .Key($@"{uninstall}\{{EA-BF6}}",
                ("DisplayName", "Battlefield™ 6"), ("Publisher", "Electronic Arts"), ("InstallLocation", @"C:\Program Files\EA Games\Battlefield 6"),
                ("UninstallString", @"""C:\Program Files\Common Files\EAInstaller\Battlefield 6\Cleanup.exe"" uninstall_game"))
            .Directory(@"C:\Program Files\EA Games\Battlefield 6")
            .Key($@"{uninstall}\EA app",
                ("DisplayName", "EA app"), ("Publisher", "Electronic Arts"), ("InstallLocation", @"C:\Program Files\Electronic Arts\EA Desktop"))
            .Directory(@"C:\Program Files\Electronic Arts\EA Desktop")
            .Key($@"{uninstall}\Notepad++", ("DisplayName", "Notepad++"), ("InstallLocation", @"C:\Program Files\Notepad++"))
            .Directory(@"C:\Program Files\Notepad++");

        var games = NewEngine().ListGames();

        Assert.Equal(
            [("Call of Duty", GameLauncher.BattleNet), ("Battlefield™ 6", GameLauncher.Ea)],
            games.Select(g => (g.Name, g.Launcher)));
    }

    [Fact]
    public void Xbox_app_games_are_found_in_the_folders_each_drive_s_GamingRoot_names()
    {
        _pc.FixedDrives.Add(@"E:\");
        _pc.File(@"E:\.GamingRoot", GamingRoot("Games\\Xbox"))
            .File(@"E:\Games\Xbox\Forza Horizon 5\Content\MicrosoftGame.config", """
                <?xml version="1.0" encoding="utf-8"?>
                <Game configVersion="1">
                  <ExecutableList><Executable Name="ForzaHorizon5.exe" Id="Game" /></ExecutableList>
                  <ShellVisuals DefaultDisplayName="Forza Horizon 5" PublisherDisplayName="Microsoft Studios" />
                </Game>
                """)
            .File(@"E:\Games\Xbox\Localized\Content\MicrosoftGame.config", """
                <Game><ExecutableList><Executable Name="bin\Game.exe" /></ExecutableList>
                <ShellVisuals DefaultDisplayName="ms-resource:AppDisplayName" /></Game>
                """)
            .Directory(@"E:\Games\Xbox\Not A Game");

        var games = NewEngine().ListGames();

        Assert.Equal(
            [
                ("Forza Horizon 5", GameLauncher.Xbox, @"E:\Games\Xbox\Forza Horizon 5", @"E:\Games\Xbox\Forza Horizon 5\Content\ForzaHorizon5.exe"),
                ("Localized", GameLauncher.Xbox, @"E:\Games\Xbox\Localized", @"E:\Games\Xbox\Localized\Content\bin\Game.exe"),
            ],
            games.Select(g => (g.Name, g.Launcher, g.InstallPath, g.ExePath)));
    }

    [Fact]
    public void A_game_added_by_its_exe_is_listed_and_kept_for_next_time()
    {
        _pc.File(@"D:\Games\Escape from Tarkov\EscapeFromTarkov.exe");

        var result = NewEngine().AddGame(@"D:\Games\Escape from Tarkov\EscapeFromTarkov.exe");

        Assert.True(result.Succeeded, result.Message);
        var expected = new InstalledGame("EscapeFromTarkov", GameLauncher.Manual, @"D:\Games\Escape from Tarkov", @"D:\Games\Escape from Tarkov\EscapeFromTarkov.exe", [AntiCheat.BattlEye]);
        Assert.Equal(expected, result.Game);
        Assert.Equal([expected], NewEngine().ListGames());
    }

    [Theory]
    [InlineData(@"D:\Games\readme.txt", "isn't a program")]
    [InlineData(@"D:\Games\Missing.exe", "couldn't be found")]
    public void Only_an_exe_that_exists_can_be_added(string path, string why)
    {
        _pc.File(@"D:\Games\readme.txt");

        var result = NewEngine().AddGame(path);

        Assert.False(result.Succeeded);
        Assert.Contains(why, result.Message);
        Assert.Empty(NewEngine().ListGames());
    }

    [Fact]
    public void A_game_already_in_the_list_is_not_added_twice()
    {
        _pc.File(@"D:\Games\Game.exe");
        NewEngine().AddGame(@"D:\Games\Game.exe");

        var again = NewEngine().AddGame(@"d:\games\GAME.EXE");

        Assert.True(again.Succeeded);
        Assert.Contains("already", again.Message);
        Assert.Single(NewEngine().ListGames());
    }

    [Fact]
    public void A_game_added_by_hand_can_be_removed()
    {
        _pc.File(@"D:\Games\Game.exe");
        NewEngine().AddGame(@"D:\Games\Game.exe");

        var result = NewEngine().RemoveGame(@"D:\Games\Game.exe");

        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(NewEngine().ListGames());
    }

    [Fact]
    public void Each_user_has_their_own_games_added_by_hand()
    {
        _pc.File(@"D:\Games\Game.exe");
        NewEngine().AddGame(@"D:\Games\Game.exe");

        var other = new Engine(new Catalogue([LabelTests.TestTweak("test.plain")]), new SystemAdapters(_registry, _system, Games: _pc), _journal, "S-1-5-21-other", gameStore: _store);

        Assert.Empty(other.ListGames());
    }

    [Fact]
    public void Known_games_carry_the_anti_cheats_they_use()
    {
        SteamGame("730", "Counter-Strike 2");
        SteamGame("359550", "Tom Clancy's Rainbow Six Siege");
        SteamGame("1172470", "Apex Legends");
        _pc.Key(@"SOFTWARE\WOW6432Node\GOG.com\Games\1", ("gameName", "Cyberpunk 2077"), ("path", @"D:\GOG\Cyberpunk 2077"))
            .Directory(@"D:\GOG\Cyberpunk 2077")
            .File(@"C:\Riot Games\VALORANT\live\VALORANT.exe");
        var engine = NewEngine();
        engine.AddGame(@"C:\Riot Games\VALORANT\live\VALORANT.exe");

        var games = engine.ListGames().ToDictionary(g => g.Name, g => g.AntiCheats);

        Assert.Equal([AntiCheat.FaceIt], games["Counter-Strike 2"]);
        Assert.Equal([AntiCheat.BattlEye], games["Tom Clancy's Rainbow Six Siege"]);
        Assert.Equal([AntiCheat.EasyAntiCheat], games["Apex Legends"]);
        Assert.Equal([AntiCheat.Vanguard], games["VALORANT"]);
        Assert.Empty(games["Cyberpunk 2077"]);
    }

    [Theory]
    [InlineData("EasyAntiCheat", AntiCheat.EasyAntiCheat)]
    [InlineData("EasyAntiCheat_EOS", AntiCheat.EasyAntiCheat)]
    [InlineData("BattlEye", AntiCheat.BattlEye)]
    public void An_unknown_game_shipping_an_anti_cheat_folder_is_marked_with_it(string folder, AntiCheat expected)
    {
        _pc.File($@"D:\Indie Shooter\{folder}\settings.json").File(@"D:\Indie Shooter\Shooter.exe");
        var engine = NewEngine();
        engine.AddGame(@"D:\Indie Shooter\Shooter.exe");

        Assert.Equal([expected], Assert.Single(engine.ListGames()).AntiCheats);
    }

    [Fact]
    public void A_tweak_with_an_anti_cheat_flag_names_the_installed_games_it_affects()
    {
        SteamGame("730", "Counter-Strike 2");
        SteamGame("359550", "Tom Clancy's Rainbow Six Siege");
        _pc.File(@"C:\Riot Games\VALORANT\live\VALORANT.exe").File(@"C:\Riot Games\League of Legends\Game\League of Legends.exe");
        var tweak = MemoryIntegrityOff();
        var engine = NewEngine(tweak);
        engine.AddGame(@"C:\Riot Games\VALORANT\live\VALORANT.exe");
        engine.AddGame(@"C:\Riot Games\League of Legends\Game\League of Legends.exe");

        var view = Assert.Single(engine.ListTweaks());

        Assert.Equal(
            [
                new GameConflict("VALORANT", AntiCheat.Vanguard, AntiCheatSeverity.WontLaunch, "VAN: RESTRICTION: 5"),
                new GameConflict("League of Legends", AntiCheat.Vanguard, AntiCheatSeverity.WontLaunch, "VAN: RESTRICTION: 5"),
                new GameConflict("Counter-Strike 2", AntiCheat.FaceIt, AntiCheatSeverity.WontLaunch, "FACEIT may require Memory Integrity"),
            ],
            view.AffectedGames.OrderBy(c => c.AntiCheat).ThenByDescending(c => c.Game));
        Assert.Equal("Turning this on may stop VALORANT, League of Legends (Riot Vanguard) and Counter-Strike 2 (FACEIT) from launching.", view.AntiCheatWarning);
    }

    [Fact]
    public void A_tweak_with_an_anti_cheat_flag_names_no_games_when_none_of_them_are_installed()
    {
        SteamGame("359550", "Tom Clancy's Rainbow Six Siege");

        var view = Assert.Single(NewEngine(MemoryIntegrityOff()).ListTweaks());

        Assert.Empty(view.AffectedGames);
        Assert.Null(view.AntiCheatWarning);
    }

    [Fact]
    public void An_anti_cheat_warning_never_blocks_an_apply()
    {
        _pc.File(@"C:\Riot Games\VALORANT\live\VALORANT.exe");
        var tweak = MemoryIntegrityOff();
        var engine = NewEngine(tweak);
        engine.AddGame(@"C:\Riot Games\VALORANT\live\VALORANT.exe");

        var result = engine.Apply(tweak.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(TweakState.On, result.Tweak!.State);
        Assert.Contains("VALORANT", result.Message);
        Assert.Contains("Riot Vanguard", result.Message);
    }

    [Fact]
    public void Launchers_are_scanned_once_per_engine_not_once_per_tweak()
    {
        SteamGame("730", "Counter-Strike 2");
        var engine = NewEngine(MemoryIntegrityOff(), MemoryIntegrityOff() with { Id = "test.second" });
        engine.ListGames();
        var reads = _pc.Reads;

        engine.ListTweaks();
        engine.ListTweaks();

        Assert.Equal(reads, _pc.Reads);
    }

    // ---- helpers ----

    private static TweakDefinition MemoryIntegrityOff() => LabelTests.TestTweak("test.hvci-off") with
    {
        AntiCheat =
        [
            new AntiCheatConflict(AntiCheat.Vanguard, AntiCheatSeverity.WontLaunch, "VAN: RESTRICTION: 5"),
            new AntiCheatConflict(AntiCheat.FaceIt, AntiCheatSeverity.WontLaunch, "FACEIT may require Memory Integrity"),
        ],
    };

    private void SteamGame(string appId, string name)
    {
        _pc.Key(@"SOFTWARE\WOW6432Node\Valve\Steam", ("InstallPath", @"C:\Steam"))
            .File($@"C:\Steam\steamapps\appmanifest_{appId}.acf", AppManifest(appId, name, name))
            .Directory($@"C:\Steam\steamapps\common\{name}");
    }

    /// <summary>The Xbox app's .GamingRoot file: "RGBX", a count, then UTF-16 folder paths relative to the drive.</summary>
    private static byte[] GamingRoot(params string[] folders) =>
    [
        .. "RGBX"u8,
        .. BitConverter.GetBytes(folders.Length),
        .. folders.SelectMany(f => System.Text.Encoding.Unicode.GetBytes(f + "\0")),
    ];

    private static string AppManifest(string appId, string name, string installDir) => $$"""
        "AppState"
        {
        	"appid"		"{{appId}}"
        	"name"		"{{name}}"
        	"installdir"		"{{installDir}}"
        }
        """;

    private Engine NewEngine(params TweakDefinition[] tweaks) => new(
        new Catalogue(tweaks.Length > 0 ? tweaks : [LabelTests.TestTweak("test.plain")]),
        new SystemAdapters(_registry, _system, Games: _pc),
        _journal,
        "S-1-5-21-test",
        gameStore: _store);
}
