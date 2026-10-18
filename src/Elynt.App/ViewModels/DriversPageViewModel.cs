using Elynt.Core.Drivers;
using Elynt.Core.Ipc;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>The Drivers page: each detected GPU with its installed driver, a lookup, and a silent install.</summary>
/// <param name="client">The current connection to the service, or null while disconnected.</param>
/// <param name="askToReboot">Shows the reboot prompt after an install whose installer said Windows must restart.</param>
public sealed class DriversPageViewModel(DriverReport report, Func<EnginePipeClient?> client, Action<DriverInstallResult> askToReboot)
{
    public IReadOnlyList<GpuItemViewModel> Gpus { get; } =
        report.Gpus.Select(g => new GpuItemViewModel(g, client, askToReboot)).ToList();

    public string? Problem { get; } = report.Problem ?? (report.Gpus.Count == 0 ? "No graphics card was found." : null);

    public bool HasProblem => Problem is not null;
}

public sealed class GpuItemViewModel(InstalledGpu gpu, Func<EnginePipeClient?> client, Action<DriverInstallResult> askToReboot) : ObservableObject
{
    private bool _useStudio;
    private bool _cleanInstall;
    private bool _isBusy;
    private string _message = "";
    private string? _vendorPage;

    public string Name => gpu.Name;

    public string InstalledVersion => $"Installed driver: {gpu.DriverVersion}";

    /// <summary>Only NVIDIA has a Studio branch.</summary>
    public bool HasBranches => gpu.Vendor == GpuVendor.Nvidia;

    public bool UseStudio
    {
        get => _useStudio;
        set => Set(ref _useStudio, value);
    }

    public bool CleanInstall
    {
        get => _cleanInstall;
        set => Set(ref _cleanInstall, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsIdle => !IsBusy;

    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    /// <summary>The vendor's driver page, shown when the app couldn't find or trust a download.</summary>
    public string? VendorPage
    {
        get => _vendorPage;
        private set
        {
            if (Set(ref _vendorPage, value))
                OnPropertyChanged(nameof(HasVendorPage));
        }
    }

    public bool HasVendorPage => VendorPage is not null;

    private DriverRequest Request => new(gpu.DeviceId, UseStudio ? DriverBranch.Studio : DriverBranch.GameReady, CleanInstall);

    public Task CheckAsync() => RunAsync("Looking up the latest driver…", async c =>
    {
        var check = await c.CheckDriverAsync(Request);
        Message = check.Message;
        VendorPage = check.Found ? null : check.VendorPage;
    });

    public Task InstallAsync() => RunAsync("Downloading, checking and installing the driver. This can take several minutes…", async c =>
    {
        var result = await c.InstallDriverAsync(Request, new Progress<DriverInstallProgress>(p => Message = p.Message));
        Log.Information("Driver install for {Gpu}: {Succeeded} reboot={Reboot} {Message}", gpu.Name, result.Succeeded, result.RebootNeeded, result.Message);
        Message = result.Message;
        VendorPage = result.Succeeded ? null : result.VendorPage;
        if (result.RebootNeeded)
            askToReboot(result);
    });

    private async Task RunAsync(string working, Func<EnginePipeClient, Task> action)
    {
        if (client() is not { } connection)
        {
            Message = "Not connected to the service.";
            return;
        }

        IsBusy = true;
        Message = working;
        try
        {
            await action(connection);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Driver command for {Gpu} failed", gpu.Name);
            Message = $"The service couldn't do this: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
