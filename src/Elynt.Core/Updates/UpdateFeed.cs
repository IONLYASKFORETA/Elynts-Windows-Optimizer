namespace Elynt.Core.Updates;

/// <summary>
/// Every build reaches beta first; Aden promotes a beta to stable by hand once he has run it. Friends stay on stable.
/// </summary>
public enum ReleaseChannel
{
    Stable,
    Beta,
}

/// <summary>
/// Where and how the app looks for updates. Builds are published to a separate public repo that holds only release
/// files, so the app needs no token: nothing secret is ever built into the app.
/// </summary>
/// <param name="RepoUrl">The public GitHub repo whose releases hold the Velopack packages.</param>
/// <param name="Channel">The Velopack channel name the packages were built with.</param>
/// <param name="IncludePrereleases">Beta builds are published as GitHub prereleases; stable ones as full releases.</param>
/// <param name="AllowDowngrade">True when switching channel, since the other channel's newest build may be older.</param>
public sealed record UpdateFeed(string RepoUrl, string Channel, bool IncludePrereleases, bool AllowDowngrade)
{
    public const string ReleasesRepo = "https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer-releases";

    public static string ChannelName(ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Beta => "beta",
        _ => "stable",
    };

    public static UpdateFeed For(ReleaseChannel chosen, string? installedChannel)
    {
        var name = ChannelName(chosen);
        return new UpdateFeed(
            ReleasesRepo,
            name,
            IncludePrereleases: chosen == ReleaseChannel.Beta,
            AllowDowngrade: !string.Equals(installedChannel, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The user's saved choice, or else the channel this copy was installed from.</summary>
    public static ReleaseChannel Choose(ReleaseChannel? saved, string? installedChannel) =>
        saved ?? (string.Equals(installedChannel, "beta", StringComparison.OrdinalIgnoreCase)
            ? ReleaseChannel.Beta
            : ReleaseChannel.Stable);
}
