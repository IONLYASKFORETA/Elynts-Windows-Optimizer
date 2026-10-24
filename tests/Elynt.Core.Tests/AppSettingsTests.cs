using Elynt.Core.Updates;

namespace Elynt.Core.Tests;

/// <summary>The app's per-user settings file: the release channel and the "Animated accents" switch.</summary>
public sealed class AppSettingsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("elynt-settings-").FullName;

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Turning_animated_accents_off_is_remembered_after_a_restart()
    {
        new AppSettingsFile(SettingsPath).Update(s => s with { AnimatedAccents = false });

        var loaded = new AppSettingsFile(SettingsPath).Load();

        Assert.False(loaded.AnimatedAccents);
    }

    [Fact]
    public void The_channel_earlier_builds_saved_is_still_read()
    {
        // What the app wrote before the Settings page existed: only the channel, Beta as its number.
        File.WriteAllText(SettingsPath, """{"UpdateChannel":1}""");

        var loaded = new AppSettingsFile(SettingsPath).Load();

        Assert.Equal(ReleaseChannel.Beta, loaded.UpdateChannel);
        Assert.Null(loaded.AnimatedAccents);
    }

    [Fact]
    public void Changing_one_setting_keeps_the_other()
    {
        var file = new AppSettingsFile(SettingsPath);
        file.Update(s => s with { UpdateChannel = ReleaseChannel.Beta });

        file.Update(s => s with { AnimatedAccents = false });

        Assert.Equal(new AppSettings(ReleaseChannel.Beta, AnimatedAccents: false), new AppSettingsFile(SettingsPath).Load());
    }

    [Fact]
    public void A_damaged_file_reads_as_nothing_chosen_yet()
    {
        File.WriteAllText(SettingsPath, "{ not json");

        Assert.Equal(new AppSettings(), new AppSettingsFile(SettingsPath).Load());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)] // the user turned animation effects off in Windows
    public void With_nothing_saved_animated_accents_follow_windows_animation_effects(bool windowsAnimations, bool expected)
    {
        Assert.Equal(expected, new AppSettings().AccentsAnimate(windowsAnimations));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_saved_choice_wins_over_windows_animation_effects(bool windowsAnimations, bool saved)
    {
        Assert.Equal(saved, new AppSettings(AnimatedAccents: saved).AccentsAnimate(windowsAnimations));
    }
}
