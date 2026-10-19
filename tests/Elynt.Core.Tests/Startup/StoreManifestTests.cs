using Elynt.Core.Startup;

namespace Elynt.Core.Tests.Startup;

/// <summary>Store apps declare their startup task in AppxManifest.xml; the real adapter reads it from there.</summary>
public class StoreManifestTests
{
    private const string Spotify = """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                 xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5">
          <Identity Name="SpotifyAB.SpotifyMusic" Publisher="CN=Spotify" Version="1.2.3.0" />
          <Properties>
            <DisplayName>Spotify Music</DisplayName>
            <PublisherDisplayName>Spotify AB</PublisherDisplayName>
          </Properties>
          <Applications>
            <Application Id="Spotify" Executable="Spotify.exe" EntryPoint="Windows.FullTrustApplication">
              <Extensions>
                <uap5:Extension Category="windows.startupTask" Executable="SpotifyStartupTask.exe" EntryPoint="Windows.FullTrustApplication">
                  <uap5:StartupTask TaskId="SpotifyStartupTask" Enabled="true" DisplayName="Spotify" />
                </uap5:Extension>
              </Extensions>
            </Application>
          </Applications>
        </Package>
        """;

    [Fact]
    public void A_declared_startup_task_is_read_with_its_name_publisher_default_and_program()
    {
        var task = Assert.Single(StoreManifest.StartupTasks(Spotify, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", @"C:\Program Files\WindowsApps\Spotify"));

        Assert.Equal(new StoreStartupTask(
            "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "SpotifyStartupTask", "Spotify", "Spotify AB", EnabledByDefault: true,
            @"C:\Program Files\WindowsApps\Spotify\SpotifyStartupTask.exe"), task);
    }

    [Fact]
    public void A_resource_name_falls_back_to_the_package_display_name_and_off_by_default_is_kept()
    {
        var manifest = Spotify
            .Replace("DisplayName=\"Spotify\"", "DisplayName=\"ms-resource:StartupName\"")
            .Replace("Enabled=\"true\"", "Enabled=\"false\"");

        var task = Assert.Single(StoreManifest.StartupTasks(manifest, "pfn", @"C:\Apps\Spotify"));

        Assert.Equal("Spotify Music", task.DisplayName);
        Assert.False(task.EnabledByDefault);
    }

    [Fact]
    public void A_manifest_without_a_startup_task_or_that_cannot_be_read_gives_none()
    {
        Assert.Empty(StoreManifest.StartupTasks(Spotify.Replace("windows.startupTask", "windows.protocol"), "pfn", @"C:\Apps"));
        Assert.Empty(StoreManifest.StartupTasks("<Package", "pfn", @"C:\Apps"));
    }
}
