using Elynt.Core.Updates;

namespace Elynt.Core.Tests.Updates;

public class UpdateFeedTests
{
    private const string PublicReleasesRepo = "https://github.com/IONLYASKFORETA/Elynts-Windows-Optimizer-releases";

    [Fact]
    public void Stable_reads_only_full_releases_of_the_stable_channel_from_the_public_releases_repo()
    {
        var feed = UpdateFeed.For(ReleaseChannel.Stable, installedChannel: "stable");

        Assert.Equal(PublicReleasesRepo, feed.RepoUrl);
        Assert.Equal("stable", feed.Channel);
        Assert.False(feed.IncludePrereleases);
        Assert.False(feed.AllowDowngrade);
    }

    [Fact]
    public void Beta_reads_prereleases_of_the_beta_channel_from_the_same_public_repo()
    {
        var feed = UpdateFeed.For(ReleaseChannel.Beta, installedChannel: "beta");

        Assert.Equal(PublicReleasesRepo, feed.RepoUrl);
        Assert.Equal("beta", feed.Channel);
        Assert.True(feed.IncludePrereleases);
        Assert.False(feed.AllowDowngrade);
    }

    [Fact]
    public void Leaving_beta_for_stable_may_install_an_older_version()
    {
        // The newest stable is usually a few builds behind the beta the user is on.
        var feed = UpdateFeed.For(ReleaseChannel.Stable, installedChannel: "beta");

        Assert.True(feed.AllowDowngrade);
    }

    [Theory]
    [InlineData("beta", ReleaseChannel.Beta)]
    [InlineData("stable", ReleaseChannel.Stable)]
    [InlineData(null, ReleaseChannel.Stable)] // a developer build that wasn't installed by Velopack
    [InlineData("win", ReleaseChannel.Stable)] // Velopack's default channel name
    public void Without_a_saved_choice_the_app_follows_the_channel_it_was_installed_from(
        string? installedChannel, ReleaseChannel expected)
    {
        Assert.Equal(expected, UpdateFeed.Choose(saved: null, installedChannel));
    }

    [Fact]
    public void A_saved_choice_wins_over_the_installed_channel()
    {
        Assert.Equal(ReleaseChannel.Beta, UpdateFeed.Choose(saved: ReleaseChannel.Beta, installedChannel: "stable"));
    }
}
