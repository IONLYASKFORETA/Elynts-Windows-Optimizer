namespace Elynt.Core.Apps;

/// <summary>The Apps section's groups, in display order.</summary>
public static class AppGroups
{
    public const string Browsers = "Browsers";
    public const string GameLaunchers = "Game launchers";
    public const string Chat = "Chat";
    public const string Media = "Media";
    public const string Utilities = "Utilities";
    public const string DeveloperTools = "Developer tools";
    public const string Runtimes = "Runtimes";

    public static IReadOnlyList<string> All { get; } =
        [Browsers, GameLaunchers, Chat, Media, Utilities, DeveloperTools, Runtimes];
}

/// <summary>One reviewed, free app, installed by its exact winget id from the community source.</summary>
/// <param name="InAllRuntimes">Installed by the "install all runtimes" tick (Visual C++, DirectX legacy and .NET).</param>
public sealed record CatalogueApp(string Id, string Name, string Group, string Description, bool InAllRuntimes = false);

/// <summary>The curated app catalogue: about 100 free apps that are safe to install silently.</summary>
public sealed class AppCatalogue
{
    private readonly Dictionary<string, CatalogueApp> _byId;

    public AppCatalogue(IEnumerable<CatalogueApp> apps)
    {
        Apps = apps.OrderBy(a => IndexOfGroup(a.Group)).ToList();
        _byId = Apps.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<CatalogueApp> Apps { get; }

    public bool TryGet(string id, out CatalogueApp app) => _byId.TryGetValue(id, out app!);

    private static int IndexOfGroup(string group)
    {
        var index = AppGroups.All.ToList().IndexOf(group);
        return index >= 0 ? index : throw new ArgumentException($"Unknown app group '{group}'.");
    }

    public static AppCatalogue Default { get; } = new(
    [
        // Browsers. Edge is already installed and is never removed, so it isn't offered.
        new("Mozilla.Firefox", "Firefox", AppGroups.Browsers, "Mozilla's open-source browser with strong tracking protection."),
        new("Google.Chrome", "Google Chrome", AppGroups.Browsers, "Google's browser, signed in to sync with your Google account."),
        new("Brave.Brave", "Brave", AppGroups.Browsers, "Chromium-based browser that blocks ads and trackers by default."),
        new("Opera.OperaGX", "Opera GX", AppGroups.Browsers, "Gaming-themed browser with RAM and CPU limiters."),
        new("Vivaldi.Vivaldi", "Vivaldi", AppGroups.Browsers, "Highly customisable Chromium-based browser with tab stacking."),
        new("LibreWolf.LibreWolf", "LibreWolf", AppGroups.Browsers, "Firefox fork focused on privacy, with telemetry removed."),
        new("Zen-Team.Zen-Browser", "Zen Browser", AppGroups.Browsers, "Firefox-based browser with vertical tabs and a calm layout."),
        new("TorProject.TorBrowser", "Tor Browser", AppGroups.Browsers, "Browses through the Tor network for anonymity."),

        // Game launchers.
        new("Valve.Steam", "Steam", AppGroups.GameLaunchers, "Valve's PC game store and launcher."),
        new("EpicGames.EpicGamesLauncher", "Epic Games Launcher", AppGroups.GameLaunchers, "Epic's store, Fortnite and the weekly free games."),
        new("GOG.Galaxy", "GOG Galaxy", AppGroups.GameLaunchers, "GOG's DRM-free store and launcher that can also list other libraries."),
        new("Blizzard.BattleNet", "Battle.net", AppGroups.GameLaunchers, "Blizzard and Activision games, including Call of Duty."),
        new("ElectronicArts.EADesktop", "EA app", AppGroups.GameLaunchers, "Electronic Arts' store and launcher (replaced Origin)."),
        new("Ubisoft.Connect", "Ubisoft Connect", AppGroups.GameLaunchers, "Ubisoft's store and launcher."),
        new("Amazon.Games", "Amazon Games", AppGroups.GameLaunchers, "Launcher for Prime Gaming free games and Amazon titles."),
        new("ItchIo.Itch", "itch.io", AppGroups.GameLaunchers, "Desktop app for indie games bought on itch.io."),
        new("Mojang.MinecraftLauncher", "Minecraft Launcher", AppGroups.GameLaunchers, "Official launcher for Minecraft Java and Bedrock."),
        new("HeroicGamesLauncher.HeroicGamesLauncher", "Heroic Games Launcher", AppGroups.GameLaunchers, "Open-source launcher for Epic, GOG and Amazon libraries."),
        new("Playnite.Playnite", "Playnite", AppGroups.GameLaunchers, "Open-source library that gathers games from every launcher in one place."),
        new("PrismLauncher.PrismLauncher", "Prism Launcher", AppGroups.GameLaunchers, "Open-source Minecraft launcher for managing modded instances."),

        // Chat.
        new("Discord.Discord", "Discord", AppGroups.Chat, "Voice, video and text chat for gaming communities."),
        new("Vencord.Vesktop", "Vesktop", AppGroups.Chat, "Lighter Discord client with better screen-share audio."),
        new("TeamSpeakSystems.TeamSpeakClient", "TeamSpeak", AppGroups.Chat, "Low-latency voice chat used by many clans and servers."),
        new("Mumble.Mumble.Client", "Mumble", AppGroups.Chat, "Open-source, low-latency voice chat."),
        new("OpenWhisperSystems.Signal", "Signal", AppGroups.Chat, "End-to-end encrypted messaging, linked to the phone app."),
        new("Telegram.TelegramDesktop", "Telegram", AppGroups.Chat, "Cloud-based messaging with large groups and channels."),
        new("SlackTechnologies.Slack", "Slack", AppGroups.Chat, "Team chat organised into channels."),
        new("Zoom.Zoom", "Zoom", AppGroups.Chat, "Video meetings and calls."),
        new("Mozilla.Thunderbird", "Thunderbird", AppGroups.Chat, "Mozilla's free email, calendar and contacts app."),

        // Media.
        new("VideoLAN.VLC", "VLC media player", AppGroups.Media, "Plays almost any video or audio file without extra codecs."),
        new("clsid2.mpc-hc", "MPC-HC", AppGroups.Media, "Lightweight, classic video player."),
        new("Spotify.Spotify", "Spotify", AppGroups.Media, "Music and podcast streaming."),
        new("PeterPawlowski.foobar2000", "foobar2000", AppGroups.Media, "Light, highly customisable music player."),
        new("OBSProject.OBSStudio", "OBS Studio", AppGroups.Media, "Records and streams gameplay; the standard free streaming tool."),
        new("Audacity.Audacity", "Audacity", AppGroups.Media, "Records and edits audio."),
        new("HandBrake.HandBrake", "HandBrake", AppGroups.Media, "Converts and compresses video files."),
        new("KDE.Kdenlive", "Kdenlive", AppGroups.Media, "Open-source video editor."),
        new("GIMP.GIMP.3", "GIMP", AppGroups.Media, "Open-source image editor."),
        new("KDE.Krita", "Krita", AppGroups.Media, "Open-source digital painting app."),
        new("dotPDN.PaintDotNet", "Paint.NET", AppGroups.Media, "Simple, fast image editor with layers."),
        new("Inkscape.Inkscape", "Inkscape", AppGroups.Media, "Open-source vector graphics editor."),
        new("BlenderFoundation.Blender", "Blender", AppGroups.Media, "Open-source 3D modelling, animation and rendering."),
        new("ShareX.ShareX", "ShareX", AppGroups.Media, "Screenshots and screen recordings with editing and upload."),
        new("NickeManarin.ScreenToGif", "ScreenToGif", AppGroups.Media, "Records a part of the screen as a GIF or short video."),
        new("Gyan.FFmpeg", "FFmpeg", AppGroups.Media, "Command-line tool that converts any audio or video format."),
        new("yt-dlp.yt-dlp", "yt-dlp", AppGroups.Media, "Command-line video downloader for many sites."),

        // Utilities.
        new("7zip.7zip", "7-Zip", AppGroups.Utilities, "Opens and creates zip, 7z, rar and other archives."),
        new("voidtools.Everything", "Everything", AppGroups.Utilities, "Finds any file on your drives instantly by name."),
        new("Microsoft.PowerToys", "PowerToys", AppGroups.Utilities, "Microsoft's power-user tools: FancyZones, PowerRename, colour picker and more."),
        new("Notepad++.Notepad++", "Notepad++", AppGroups.Utilities, "Fast text and code editor with tabs."),
        new("Bitwarden.Bitwarden", "Bitwarden", AppGroups.Utilities, "Open-source password manager that syncs across devices."),
        new("KeePassXCTeam.KeePassXC", "KeePassXC", AppGroups.Utilities, "Offline, open-source password manager."),
        new("CPUID.CPU-Z", "CPU-Z", AppGroups.Utilities, "Shows CPU, motherboard and RAM details."),
        new("TechPowerUp.GPU-Z", "GPU-Z", AppGroups.Utilities, "Shows graphics card details and sensors."),
        new("REALiX.HWiNFO", "HWiNFO", AppGroups.Utilities, "Detailed hardware information and sensor monitoring."),
        new("LibreHardwareMonitor.LibreHardwareMonitor", "Libre Hardware Monitor", AppGroups.Utilities, "Open-source temperature, fan and clock monitor."),
        new("CrystalDewWorld.CrystalDiskInfo", "CrystalDiskInfo", AppGroups.Utilities, "Shows drive health and temperature from SMART data."),
        new("CrystalDewWorld.CrystalDiskMark", "CrystalDiskMark", AppGroups.Utilities, "Measures drive read and write speeds."),
        new("Guru3D.Afterburner", "MSI Afterburner", AppGroups.Utilities, "GPU overclocking and fan control with an in-game overlay."),
        new("Guru3D.RTSS", "RivaTuner Statistics Server", AppGroups.Utilities, "Frame-rate limiter and on-screen display for games."),
        new("WinDirStat.WinDirStat", "WinDirStat", AppGroups.Utilities, "Shows what is using space on a drive."),
        new("AntibodySoftware.WizTree", "WizTree", AppGroups.Utilities, "Very fast disk space analyser."),
        new("Rufus.Rufus", "Rufus", AppGroups.Utilities, "Makes bootable USB drives from ISO files."),
        new("qBittorrent.qBittorrent", "qBittorrent", AppGroups.Utilities, "Open-source, ad-free torrent client."),
        new("Malwarebytes.Malwarebytes", "Malwarebytes", AppGroups.Utilities, "Second-opinion malware scanner (the free scanner needs no subscription)."),
        new("TheDocumentFoundation.LibreOffice", "LibreOffice", AppGroups.Utilities, "Free office suite that opens Word, Excel and PowerPoint files."),
        new("SumatraPDF.SumatraPDF", "SumatraPDF", AppGroups.Utilities, "Small, fast PDF and e-book reader."),
        new("LocalSend.LocalSend", "LocalSend", AppGroups.Utilities, "Sends files to nearby phones and PCs over your own network."),
        new("Klocman.BulkCrapUninstaller", "Bulk Crap Uninstaller", AppGroups.Utilities, "Removes many programs at once, including leftovers."),
        new("Microsoft.Sysinternals.Autoruns", "Autoruns", AppGroups.Utilities, "Microsoft's complete list of everything that starts with Windows."),
        new("Microsoft.Sysinternals.ProcessExplorer", "Process Explorer", AppGroups.Utilities, "Microsoft's detailed Task Manager replacement."),
        new("Proton.ProtonVPN", "Proton VPN", AppGroups.Utilities, "VPN with an unlimited free tier and a no-logs policy."),

        // Developer tools.
        new("Git.Git", "Git", AppGroups.DeveloperTools, "Version control, with Git Bash."),
        new("GitHub.GitHubDesktop", "GitHub Desktop", AppGroups.DeveloperTools, "Simple Git app for GitHub repositories."),
        new("Microsoft.VisualStudioCode", "Visual Studio Code", AppGroups.DeveloperTools, "Microsoft's free code editor with extensions."),
        new("VSCodium.VSCodium", "VSCodium", AppGroups.DeveloperTools, "Visual Studio Code built without Microsoft telemetry."),
        new("Microsoft.VisualStudio.2022.Community", "Visual Studio Community", AppGroups.DeveloperTools, "Microsoft's full IDE, free for individuals."),
        new("JetBrains.Toolbox", "JetBrains Toolbox", AppGroups.DeveloperTools, "Installs and updates JetBrains IDEs, including the free editions."),
        new("Microsoft.WindowsTerminal", "Windows Terminal", AppGroups.DeveloperTools, "Tabbed terminal for PowerShell, Command Prompt and WSL."),
        new("Microsoft.PowerShell", "PowerShell 7", AppGroups.DeveloperTools, "The current cross-platform PowerShell."),
        new("Python.Python.3.13", "Python 3.13", AppGroups.DeveloperTools, "The Python language and pip."),
        new("OpenJS.NodeJS.LTS", "Node.js LTS", AppGroups.DeveloperTools, "JavaScript runtime with npm, long-term support release."),
        new("Microsoft.DotNet.SDK.10", ".NET 10 SDK", AppGroups.DeveloperTools, "Builds .NET apps (C#, F#)."),
        new("Rustlang.Rustup", "Rustup", AppGroups.DeveloperTools, "Installs the Rust toolchain."),
        new("Docker.DockerDesktop", "Docker Desktop", AppGroups.DeveloperTools, "Runs containers; free for personal use."),
        new("WinMerge.WinMerge", "WinMerge", AppGroups.DeveloperTools, "Compares and merges files and folders."),

        // Runtimes. The "install all runtimes" tick covers Visual C++, DirectX legacy and .NET.
        new("Microsoft.VCRedist.2015+.x64", "Visual C++ 2015–2022 (64-bit)", AppGroups.Runtimes, "Runtime most current games and apps need.", InAllRuntimes: true),
        new("Microsoft.VCRedist.2015+.x86", "Visual C++ 2015–2022 (32-bit)", AppGroups.Runtimes, "Runtime 32-bit games and launchers need.", InAllRuntimes: true),
        new("Microsoft.VCRedist.2013.x64", "Visual C++ 2013 (64-bit)", AppGroups.Runtimes, "Runtime for games built around 2014–2016.", InAllRuntimes: true),
        new("Microsoft.VCRedist.2013.x86", "Visual C++ 2013 (32-bit)", AppGroups.Runtimes, "32-bit runtime for games built around 2014–2016.", InAllRuntimes: true),
        new("Microsoft.VCRedist.2012.x64", "Visual C++ 2012 (64-bit)", AppGroups.Runtimes, "Runtime for games built around 2013.", InAllRuntimes: true),
        new("Microsoft.VCRedist.2012.x86", "Visual C++ 2012 (32-bit)", AppGroups.Runtimes, "32-bit runtime for games built around 2013.", InAllRuntimes: true),
        new("Microsoft.VCRedist.2010.x64", "Visual C++ 2010 (64-bit)", AppGroups.Runtimes, "Runtime for older games built around 2010–2012.", InAllRuntimes: true),
        new("Microsoft.VCRedist.2010.x86", "Visual C++ 2010 (32-bit)", AppGroups.Runtimes, "32-bit runtime for older games built around 2010–2012.", InAllRuntimes: true),
        new("Microsoft.VCRedist.2008.x64", "Visual C++ 2008 (64-bit)", AppGroups.Runtimes, "Runtime for games from the late 2000s.", InAllRuntimes: true),
        new("Microsoft.VCRedist.2008.x86", "Visual C++ 2008 (32-bit)", AppGroups.Runtimes, "32-bit runtime for games from the late 2000s.", InAllRuntimes: true),
        new("Microsoft.VCRedist.2005.x64", "Visual C++ 2005 (64-bit)", AppGroups.Runtimes, "Runtime for games from the mid 2000s.", InAllRuntimes: true),
        new("Microsoft.VCRedist.2005.x86", "Visual C++ 2005 (32-bit)", AppGroups.Runtimes, "32-bit runtime for games from the mid 2000s.", InAllRuntimes: true),
        new("Microsoft.DirectX", "DirectX End-User Runtime", AppGroups.Runtimes, "Legacy DirectX 9–11 files (d3dx9, XAudio2_7) older games still load.", InAllRuntimes: true),
        new("Microsoft.DotNet.DesktopRuntime.8", ".NET 8 Desktop Runtime", AppGroups.Runtimes, "Runtime for Windows apps built on .NET 8.", InAllRuntimes: true),
        new("Microsoft.DotNet.DesktopRuntime.9", ".NET 9 Desktop Runtime", AppGroups.Runtimes, "Runtime for Windows apps built on .NET 9.", InAllRuntimes: true),
        new("Microsoft.DotNet.DesktopRuntime.10", ".NET 10 Desktop Runtime", AppGroups.Runtimes, "Runtime for Windows apps built on .NET 10.", InAllRuntimes: true),
        new("Microsoft.XNARedist", "XNA Framework 4.0", AppGroups.Runtimes, "Runtime for older indie games made with XNA (Terraria, Stardew Valley's first releases)."),
        new("EclipseAdoptium.Temurin.21.JRE", "Java 21 (Temurin JRE)", AppGroups.Runtimes, "Open-source Java runtime for Java apps and modded Minecraft servers."),
    ]);
}
