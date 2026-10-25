using Elynt.Core.Drivers;
using Elynt.Core.Ipc;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>
/// The Drivers page: each detected GPU, then the chipset driver for the CPU's maker, with its installed driver, a
/// lookup, and a silent install.
/// </summary>
/// <param name="client">The current connection to the service, or null while disconnected.</param>
/// <param name="askToReboot">Shows the reboot prompt after an install whose installer said Windows must restart.</param>
public sealed class DriversPageViewModel(DriverReport report, Func<EnginePipeClient?> client, Action<DriverInstallResult> askToReboot)
{
    public IReadOnlyList<GpuItemViewModel> Gpus { get; } =
        report.Gpus
            .Concat(report.Chipset is { } chipset
                ? [new InstalledGpu(chipset.Name, DriverRequest.Chipset, chipset.Vendor, chipset.DriverVersion ?? "not installed", "")]
                : [])
            .Select(g => new GpuItemViewModel(g, client, askToReboot)).ToList();

    public string? Problem { get; } = report.Problem ?? (report.Gpus.Count == 0 ? "No graphics card was found." : null);

    public bool HasProblem => Problem is not null;

    /// <summary>Whether any driver has been checked since the service connected.</summary>
    public bool Checked => Gpus.Any(g => g.UpdateFound is not null);

    /// <summary>How many drivers had a newer one at their last check, for the Home page.</summary>
    public int UpdatesFound => Gpus.Count(g => g.UpdateFound == true);

    /// <summary>Looks up the latest driver for every GPU and the chipset, as each one's own check button does.</summary>
    public Task CheckAllAsync() => Task.WhenAll(Gpus.Select(g => g.CheckAsync()));

    /// <summary>Problem-driver warnings, old-driver cleaning and its backups. Loads itself.</summary>
    public DriverCleaningViewModel Cleaning { get; } = Loaded(new DriverCleaningViewModel(client, askToReboot));

    private static DriverCleaningViewModel Loaded(DriverCleaningViewModel cleaning)
    {
        _ = cleaning.LoadAsync();
        return cleaning;
    }
}

public sealed class GpuItemViewModel(InstalledGpu gpu, Func<EnginePipeClient?> client, Action<DriverInstallResult> askToReboot) : ObservableObject
{
    private bool _useStudio;
    private bool _cleanInstall;
    private bool _leanInstall;
    private bool _isBusy;
    private string _message = "";
    private string? _vendorPage;

    public string Name => gpu.Name;

    public string InstalledVersion => $"Installed driver: {gpu.DriverVersion}";

    private bool IsNvidiaGpu => gpu.Vendor == GpuVendor.Nvidia && gpu.DeviceId != DriverRequest.Chipset;

    /// <summary>Only NVIDIA has a Studio branch.</summary>
    public bool HasBranches => IsNvidiaGpu;

    /// <summary>Only NVIDIA's installer has a clean-install switch.</summary>
    public bool HasCleanInstall => IsNvidiaGpu;

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

    /// <summary>Only NVIDIA's package can be stripped to the display driver.</summary>
    public bool CanInstallLean => IsNvidiaGpu;

    public bool LeanInstall
    {
        get => _leanInstall;
        set => Set(ref _leanInstall, value);
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
            if (!Set(ref _vendorPage, value))
                return;
            OnPropertyChanged(nameof(HasVendorPage));
            OnPropertyChanged(nameof(VendorLink));
        }
    }

    public bool HasVendorPage => VendorPage is not null;

    /// <summary>The vendor page as a link target; a Uri so the link binds without converting a missing page.</summary>
    public Uri? VendorLink => Uri.TryCreate(VendorPage, UriKind.Absolute, out var link) ? link : null;

    /// <summary>Whether the last check found a newer driver, or null before the first check.</summary>
    public bool? UpdateFound { get; private set; }

    /// <summary>Back up and remove this vendor's old driver packages before installing (pnputil; restorable).</summary>
    public bool RemoveOldDrivers
    {
        get => _removeOldDrivers;
        set => Set(ref _removeOldDrivers, value);
    }

    private bool _removeOldDrivers;

    private DriverRequest Request =>
        new(gpu.DeviceId, UseStudio ? DriverBranch.Studio : DriverBranch.GameReady, CleanInstall && HasCleanInstall, LeanInstall && CanInstallLean,
            RemoveOldDrivers);

    public Task CheckAsync() => RunAsync("Looking up the latest driver…", async c =>
    {
        var check = await c.CheckDriverAsync(Request);
        UpdateFound = check.Found && check.UpdateAvailable;
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
