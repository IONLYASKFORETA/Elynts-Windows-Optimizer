using Elynt.Core.Setup;

namespace Elynt.Core.Tests.Setup;

public class ServiceSetupTests
{
    private const string Bundled = @"C:\Users\aden\AppData\Local\ElyntOptimizer\current\service";
    private const string Installed = @"C:\Program Files\ElyntOptimizer\service";
    private static readonly string InstalledExe = Path.Combine(Installed, ServiceSetup.ExeName);

    private readonly FakeServiceControl _services = new();
    private readonly FakeServiceFiles _files;

    public ServiceSetupTests() => _files = new FakeServiceFiles(_services);

    private ServiceSetup NewSetup() => new(_services, _files, new ServiceLayout(Bundled, Installed));

    [Fact]
    public void On_a_fresh_PC_the_service_is_installed_from_the_app_and_started()
    {
        _files.Put(Bundled, "1.0.5");
        var setup = NewSetup();
        Assert.Equal(ServiceSetupState.NotInstalled, setup.Check());

        var result = setup.InstallOrUpdate();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(ServiceSetupState.Ready, setup.Check());
        var service = _services.Get(ServiceSetup.ServiceName)!;
        Assert.True(service.Running);
        Assert.True(service.StartsAutomatically);
        Assert.True(service.RestartsOnFailure);
        Assert.Equal(InstalledExe, service.BinaryPath);
        Assert.Equal("1.0.5", _files.VersionAt(Installed));
    }

    [Fact]
    public void After_an_app_update_the_running_older_service_is_stopped_replaced_and_restarted()
    {
        _files.Put(Bundled, "1.0.6");
        _files.Put(Installed, "1.0.5");
        _services.Add(ServiceSetup.ServiceName, InstalledExe, running: true);
        var setup = NewSetup();
        Assert.Equal(ServiceSetupState.Outdated, setup.Check());

        var result = setup.InstallOrUpdate();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(ServiceSetupState.Ready, setup.Check());
        Assert.Equal("1.0.6", _files.VersionAt(Installed));
        Assert.True(_services.Get(ServiceSetup.ServiceName)!.Running);
    }

    [Fact]
    public void Switching_to_an_older_build_on_another_channel_also_replaces_the_service()
    {
        _files.Put(Bundled, "1.0.4");
        _files.Put(Installed, "1.0.6");
        _services.Add(ServiceSetup.ServiceName, InstalledExe, running: true);
        var setup = NewSetup();
        Assert.Equal(ServiceSetupState.Outdated, setup.Check());

        Assert.True(setup.InstallOrUpdate().Succeeded);

        Assert.Equal("1.0.4", _files.VersionAt(Installed));
        Assert.Equal(ServiceSetupState.Ready, setup.Check());
    }

    [Fact]
    public void A_service_left_by_an_older_install_at_another_path_is_moved_to_the_app_managed_one()
    {
        _files.Put(Bundled, "1.0.6");
        _services.Add(ServiceSetup.ServiceName, @"C:\ElyntOptimizer\service\Elynt.Service.exe", running: true);
        var setup = NewSetup();
        Assert.Equal(ServiceSetupState.Outdated, setup.Check());

        Assert.True(setup.InstallOrUpdate().Succeeded);

        var service = _services.Get(ServiceSetup.ServiceName)!;
        Assert.Equal(InstalledExe, service.BinaryPath);
        Assert.True(service.Running);
        Assert.True(service.RestartsOnFailure);
    }

    [Fact]
    public void A_current_but_stopped_service_is_started_again()
    {
        _files.Put(Bundled, "1.0.6");
        _files.Put(Installed, "1.0.6");
        _services.Add(ServiceSetup.ServiceName, InstalledExe, running: false);
        var setup = NewSetup();
        Assert.Equal(ServiceSetupState.Stopped, setup.Check());

        Assert.True(setup.InstallOrUpdate().Succeeded);

        Assert.Equal(ServiceSetupState.Ready, setup.Check());
    }

    [Fact]
    public void When_the_new_files_cannot_be_copied_the_old_service_is_started_again_and_the_failure_reported()
    {
        _files.Put(Bundled, "1.0.6");
        _files.Put(Installed, "1.0.5");
        _services.Add(ServiceSetup.ServiceName, InstalledExe, running: true);
        _files.CopyFails = true;

        var result = NewSetup().InstallOrUpdate();

        Assert.False(result.Succeeded);
        Assert.Contains("Disk full", result.Message);
        Assert.True(_services.Get(ServiceSetup.ServiceName)!.Running);
        Assert.Equal("1.0.5", _files.VersionAt(Installed));
    }

    [Fact]
    public void A_service_that_will_not_start_is_reported_not_thrown()
    {
        _files.Put(Bundled, "1.0.6");
        _services.StartFails = _ => true;

        var result = NewSetup().InstallOrUpdate();

        Assert.False(result.Succeeded);
        Assert.Contains("did not start", result.Message);
    }

    [Fact]
    public void A_developer_build_without_a_bundled_service_changes_nothing()
    {
        _services.Add(ServiceSetup.ServiceName, @"C:\dev\Elynt.Service.exe", running: true);
        var setup = NewSetup();
        Assert.Equal(ServiceSetupState.NoBundledService, setup.Check());

        var result = setup.InstallOrUpdate();

        Assert.False(result.Succeeded);
        Assert.Equal(@"C:\dev\Elynt.Service.exe", _services.Get(ServiceSetup.ServiceName)!.BinaryPath);
    }

    [Fact]
    public void Removing_stops_and_unregisters_the_service_and_deletes_its_files()
    {
        _files.Put(Bundled, "1.0.6");
        _files.Put(Installed, "1.0.6");
        _services.Add(ServiceSetup.ServiceName, InstalledExe, running: true);
        var setup = NewSetup();

        var result = setup.Remove();

        Assert.True(result.Succeeded, result.Message);
        Assert.Null(_services.Get(ServiceSetup.ServiceName));
        Assert.False(_files.Exists(Installed));
        Assert.Equal(ServiceSetupState.NotInstalled, setup.Check());
    }

    [Fact]
    public void Removing_when_nothing_is_installed_succeeds()
    {
        Assert.True(NewSetup().Remove().Succeeded);
    }
}
