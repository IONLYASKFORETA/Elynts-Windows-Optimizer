using Elynt.Core.Updates;
using Serilog;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace Elynt.App;

/// <summary>
/// Self-update through Velopack from the public releases repo. No token: the repo is public and holds only builds.
/// After an update the app restarts, sees its service is outdated and updates that too (see ServiceBootstrap).
/// </summary>
public sealed class AppUpdater
{
    private UpdateManager? _manager;
    private UpdateInfo? _ready;

    /// <summary>The channel this copy was installed from, or null for a developer build.</summary>
    public static string? InstalledChannel =>
        VelopackLocator.IsCurrentSet ? VelopackLocator.Current.Channel : null;

    public ReleaseChannel Channel { get; private set; } = UpdateFeed.Choose(UserSettings.Load().UpdateChannel, InstalledChannel);

    public string? ReadyVersion => _ready?.TargetFullRelease.Version.ToString();

    public void ChangeChannel(ReleaseChannel channel)
    {
        Channel = channel;
        _ready = null;
        UserSettings.Update(s => s with { UpdateChannel = channel });
    }

    /// <summary>Checks the chosen channel and downloads any newer build. Returns its version, or null.</summary>
    public async Task<string?> DownloadUpdateAsync()
    {
        var feed = UpdateFeed.For(Channel, InstalledChannel);
        _manager = new UpdateManager(
            new GithubSource(feed.RepoUrl, accessToken: null, prerelease: feed.IncludePrereleases),
            new UpdateOptions { ExplicitChannel = feed.Channel, AllowVersionDowngrade = feed.AllowDowngrade });

        if (!_manager.IsInstalled)
            return null; // a developer build run from the source tree

        var update = await _manager.CheckForUpdatesAsync();
        if (update is null)
            return null;

        Log.Information("Downloading update {Version} from the {Channel} channel", update.TargetFullRelease.Version, feed.Channel);
        await _manager.DownloadUpdatesAsync(update);
        _ready = update;
        return ReadyVersion;
    }

    public void RestartIntoUpdate()
    {
        if (_manager is null || _ready is null)
            return;
        _manager.ApplyUpdatesAndRestart(_ready.TargetFullRelease);
    }
}
