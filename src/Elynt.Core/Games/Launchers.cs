using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Text.Json;

namespace Elynt.Core.Games;

/// <summary>A game as a launcher records it, before anti-cheats are matched.</summary>
/// <param name="LauncherId">The launcher's own id (Steam app id, Epic AppName, ...), when it has one.</param>
internal sealed record FoundGame(string Name, GameLauncher Launcher, string InstallPath, string? ExePath = null, string? LauncherId = null);

/// <summary>Reads each launcher's own records of what it installed.</summary>
internal static class Launchers
{
    public static IEnumerable<FoundGame> Steam(IGameSources pc)
    {
        var steam = pc.Value(@"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath") ?? pc.Value(@"SOFTWARE\Valve\Steam", "InstallPath");
        if (steam is null)
            yield break;

        var libraries = new List<string> { steam };
        if (pc.ReadText(WinPath.Join(steam, @"steamapps\libraryfolders.vdf")) is { } vdf)
        {
            var folders = KeyValues.Parse(vdf).Block("libraryfolders");
            foreach (var folder in folders?.Blocks.Values ?? Enumerable.Empty<KeyValues>())
                if (folder["path"] is { } path && !libraries.Any(l => WinPath.Same(l, path)))
                    libraries.Add(path);
        }

        foreach (var library in libraries)
        {
            var steamapps = WinPath.Join(library, "steamapps");
            foreach (var manifest in pc.Files(steamapps, ".acf").Order(StringComparer.OrdinalIgnoreCase))
            {
                if (pc.ReadText(manifest) is not { } text || KeyValues.Parse(text).Block("AppState") is not { } app)
                    continue;
                if (app["name"] is not { } name || app["installdir"] is not { } dir || app["appid"] is not { } appId)
                    continue;
                if (SteamTools.Contains(appId))
                    continue;
                var installPath = WinPath.Join(steamapps, "common", dir);
                if (pc.DirectoryExists(installPath))
                    yield return new FoundGame(name, GameLauncher.Steam, installPath, LauncherId: appId);
            }
        }
    }

    /// <summary>The Epic Games Launcher keeps one JSON manifest per installed game in ProgramData.</summary>
    public static IEnumerable<FoundGame> Epic(IGameSources pc)
    {
        var manifests = WinPath.Join(pc.ProgramData, @"Epic\EpicGamesLauncher\Data\Manifests");
        foreach (var file in pc.Files(manifests, ".item").Order(StringComparer.OrdinalIgnoreCase))
            if (pc.ReadText(file) is { } text && EpicManifest(text) is { } game && pc.DirectoryExists(game.InstallPath))
                yield return game;
    }

    private static FoundGame? EpicManifest(string text)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True)
                return null;
            if (String(root, "DisplayName") is not { } name || String(root, "InstallLocation") is not { Length: > 0 } location)
                return null;
            var exe = String(root, "LaunchExecutable") is { Length: > 0 } launch ? WinPath.Join(location, launch) : null;
            return new FoundGame(name, GameLauncher.Epic, WinPath.Join(location), exe, String(root, "AppName"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>GOG Galaxy and the offline GOG installers both register each game under GOG.com\Games.</summary>
    public static IEnumerable<FoundGame> Gog(IGameSources pc)
    {
        const string games = @"SOFTWARE\WOW6432Node\GOG.com\Games";
        foreach (var id in pc.SubKeys(games))
        {
            var key = $@"{games}\{id}";
            var name = pc.Value(key, "gameName");
            var path = pc.Value(key, "path");
            if (name is null || path is null || !pc.DirectoryExists(path))
                continue;
            yield return new FoundGame(name, GameLauncher.Gog, WinPath.Join(path), pc.Value(key, "exe") is { Length: > 0 } exe ? WinPath.Join(exe) : null, id);
        }
    }

    /// <summary>Ubisoft Connect records each install's folder; the folder is named after the game.</summary>
    public static IEnumerable<FoundGame> Ubisoft(IGameSources pc)
    {
        const string installs = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";
        foreach (var id in pc.SubKeys(installs))
        {
            if (pc.Value($@"{installs}\{id}", "InstallDir") is not { Length: > 0 } dir || !pc.DirectoryExists(dir))
                continue;
            var path = WinPath.Join(dir);
            yield return new FoundGame(WinPath.FileName(path), GameLauncher.Ubisoft, path, LauncherId: id);
        }
    }

    /// <summary>Battle.net games register an uninstall entry that runs the Blizzard Uninstaller.</summary>
    public static IEnumerable<FoundGame> BattleNet(IGameSources pc) => UninstallEntries(pc)
        .Where(e => Has(e.UninstallString, "Blizzard Uninstaller") || Has(e.UninstallString, @"\Battle.net\"))
        .Where(e => !e.Name.Equals("Battle.net", StringComparison.OrdinalIgnoreCase))
        .Select(e => new FoundGame(e.Name, GameLauncher.BattleNet, e.InstallPath, LauncherId: e.Id));

    /// <summary>EA app (and Origin) games register an uninstall entry published by Electronic Arts or run by EAInstaller.</summary>
    public static IEnumerable<FoundGame> Ea(IGameSources pc) => UninstallEntries(pc)
        .Where(e => Has(e.Publisher, "Electronic Arts") || Has(e.UninstallString, "EAInstaller"))
        .Where(e => !EaLaunchers.Contains(e.Name))
        .Select(e => new FoundGame(e.Name, GameLauncher.Ea, e.InstallPath, LauncherId: e.Id));

    private static readonly HashSet<string> EaLaunchers = new(StringComparer.OrdinalIgnoreCase) { "EA app", "EA Desktop", "Origin" };

    private sealed record UninstallEntry(string Id, string Name, string InstallPath, string? Publisher, string? UninstallString);

    private static IEnumerable<UninstallEntry> UninstallEntries(IGameSources pc)
    {
        foreach (var root in (string[])[@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"])
            foreach (var id in pc.SubKeys(root))
            {
                var key = $@"{root}\{id}";
                var name = pc.Value(key, "DisplayName");
                var location = pc.Value(key, "InstallLocation")?.Trim('"');
                if (name is null || location is not { Length: > 0 } || !pc.DirectoryExists(location))
                    continue;
                yield return new UninstallEntry(id, name, WinPath.Join(location), pc.Value(key, "Publisher"), pc.Value(key, "UninstallString"));
            }
    }

    private static bool Has(string? text, string part) => text?.Contains(part, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// The Xbox app writes a .GamingRoot file at the root of each drive it installs to, naming the install folders
    /// (XboxGames by default). Each game folder has Content\MicrosoftGame.config with its name and executable.
    /// </summary>
    public static IEnumerable<FoundGame> Xbox(IGameSources pc)
    {
        foreach (var drive in pc.Drives)
        {
            var roots = GamingRootFolders(pc.ReadBytes(WinPath.Join(drive, ".GamingRoot"))).ToList();
            if (roots.Count == 0)
                roots.Add("XboxGames");
            foreach (var root in roots)
                foreach (var folder in pc.Directories(WinPath.Join(drive, root)).Order(StringComparer.OrdinalIgnoreCase))
                    if (XboxGame(pc, WinPath.Join(folder)) is { } game)
                        yield return game;
        }
    }

    private static FoundGame? XboxGame(IGameSources pc, string folder)
    {
        var content = WinPath.Join(folder, "Content");
        if (pc.ReadText(WinPath.Join(content, "MicrosoftGame.config")) is not { } text)
            return null;
        XElement config;
        try
        {
            config = XElement.Parse(text.TrimStart('﻿'));
        }
        catch (XmlException)
        {
            return null;
        }
        var shown = config.Descendants().FirstOrDefault(e => e.Name.LocalName == "ShellVisuals")?.Attribute("DefaultDisplayName")?.Value;
        var name = shown is { Length: > 0 } && !shown.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? shown : WinPath.FileName(folder);
        var exe = config.Descendants().FirstOrDefault(e => e.Name.LocalName == "Executable")?.Attribute("Name")?.Value;
        return new FoundGame(name, GameLauncher.Xbox, folder, exe is { Length: > 0 } ? WinPath.Join(content, exe) : null);
    }

    /// <summary>"RGBX", a 4-byte count, then null-terminated UTF-16 folder paths relative to the drive root.</summary>
    private static IEnumerable<string> GamingRootFolders(byte[]? bytes)
    {
        if (bytes is not { Length: > 8 } || !bytes.AsSpan(0, 4).SequenceEqual("RGBX"u8))
            return [];
        return Encoding.Unicode.GetString(bytes, 8, (bytes.Length - 8) & ~1)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Steam installs these alongside games; they are not games.</summary>
    private static readonly HashSet<string> SteamTools = ["228980", "1070560", "1391110", "1628350", "1493710"];
}

/// <summary>Windows path helpers that behave the same on every OS the tests run on.</summary>
internal static class WinPath
{
    public static string Join(string first, params string[] rest) =>
        rest.Aggregate(first.Replace('/', '\\').TrimEnd('\\'), (path, part) => path + "\\" + part.Replace('/', '\\').Trim('\\'));

    public static bool Same(string a, string b) =>
        string.Equals(a.Replace('/', '\\').TrimEnd('\\'), b.Replace('/', '\\').TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public static string FileName(string path) => path.Replace('/', '\\').TrimEnd('\\').Split('\\')[^1];

    public static string Parent(string path)
    {
        var trimmed = path.Replace('/', '\\').TrimEnd('\\');
        var slash = trimmed.LastIndexOf('\\');
        return slash <= 0 ? trimmed : slash == 2 && trimmed[1] == ':' ? trimmed[..3] : trimmed[..slash];
    }
}
