using System.Collections.ObjectModel;
using Elynt.Core.Drivers;
using Elynt.Core.Ipc;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>
/// The Drivers page's cleaning panel: warnings for drivers that stop anti-cheats, removing a vendor's old drivers
/// (backed up, restorable), the opt-in DDU deep clean, and the backups to restore.
/// </summary>
public sealed class DriverCleaningViewModel(Func<EnginePipeClient?> client, Action<DriverInstallResult> askToReboot) : ObservableObject
{
    private bool _isBusy;
    private bool _dduAccepted;
    private string _message = "";
    private string _dduLabel = "";
    private GpuVendor _vendor = GpuVendor.Nvidia;

    public ObservableCollection<ProblemDriver> ProblemDrivers { get; } = [];

    public ObservableCollection<DriverBackupItemViewModel> Backups { get; } = [];

    public IReadOnlyList<GpuVendor> Vendors { get; } = [GpuVendor.Nvidia, GpuVendor.Amd, GpuVendor.Intel];

    public GpuVendor Vendor
    {
        get => _vendor;
        set => Set(ref _vendor, value);
    }

    /// <summary>Says DDU is a third-party tool, downloaded when used. Shown next to the box that opts in to it.</summary>
    public string DduLabel
    {
        get => _dduLabel;
        private set => Set(ref _dduLabel, value);
    }

    /// <summary>The user ticked "I understand" under <see cref="DduLabel"/>. DDU won't run without it.</summary>
    public bool DduAccepted
    {
        get => _dduAccepted;
        set => Set(ref _dduAccepted, value);
    }

    public bool HasProblemDrivers => ProblemDrivers.Count > 0;

    public bool HasBackups => Backups.Count > 0;

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

    public Task LoadAsync() => RunAsync(null, async c =>
    {
        var report = await c.GetDriverCleaningAsync();
        DduLabel = report.DduLabel;
        ProblemDrivers.Clear();
        foreach (var problem in report.ProblemDrivers)
            ProblemDrivers.Add(problem);
        Backups.Clear();
        foreach (var backup in report.Backups)
            Backups.Add(new DriverBackupItemViewModel(backup, this));
        OnPropertyChanged(nameof(HasProblemDrivers));
        OnPropertyChanged(nameof(HasBackups));
        if (report.Problem is not null)
            Message = report.Problem;
    });

    public Task CleanAsync() => Clean(DriverCleanMethod.Pnputil, "Backing up and removing the old drivers…");

    public Task DeepCleanAsync() => Clean(DriverCleanMethod.Ddu, "Downloading DDU from Wagnardsoft and cleaning. This can take several minutes…");

    public Task RestoreAsync(DriverBackupItemViewModel backup) => RunAsync("Putting the drivers back…", async c =>
    {
        var result = await c.RestoreDriversAsync(backup.Id);
        Log.Information("Driver restore {Backup}: {Succeeded} {Message}", backup.Id, result.Succeeded, result.Message);
        Report(result);
    });

    private Task Clean(DriverCleanMethod method, string working) => RunAsync(working, async c =>
    {
        var result = await c.CleanDriversAsync(new DriverCleanRequest(Vendor, method, method == DriverCleanMethod.Ddu && DduAccepted));
        Log.Information("Driver clean {Vendor} with {Method}: {Succeeded} {Message}", Vendor, method, result.Succeeded, result.Message);
        Report(result);
    });

    private void Report(DriverCleanResult result)
    {
        Message = result.Message;
        if (result.RebootNeeded)
            askToReboot(new DriverInstallResult(result.Succeeded, true, result.Message));
        _ = LoadAsync();
    }

    private async Task RunAsync(string? working, Func<EnginePipeClient, Task> action)
    {
        if (client() is not { } connection)
        {
            Message = "Not connected to the service.";
            return;
        }

        IsBusy = true;
        if (working is not null)
            Message = working;
        try
        {
            await action(connection);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Driver cleaning command failed");
            Message = $"The service couldn't do this: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed class DriverBackupItemViewModel(DriverBackup backup, DriverCleaningViewModel owner)
{
    public string Id => backup.Id;

    public Task RestoreAsync() => owner.RestoreAsync(this);

    public string Title => $"{backup.Vendor} drivers removed {backup.Created.ToLocalTime():g}";

    public string Contents => string.Join(", ", backup.Packages);
}
