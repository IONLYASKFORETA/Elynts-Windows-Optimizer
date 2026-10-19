using System.Collections.ObjectModel;
using System.Windows.Threading;
using Elynt.Core.Ipc;
using Elynt.Core.LowLevel;
using Elynt.Core.Sensors;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>
/// The Overclocking page's Sensors panel: live readouts, and the optional PawnIO driver with its FACEIT warning,
/// "Turn on PawnIO" and "Uninstall driver". Readouts refresh every two seconds while the page is showing.
/// </summary>
public sealed class SensorsPageViewModel : ObservableObject
{
    private readonly Func<EnginePipeClient?> _client;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(2) };
    private PawnIoReport _pawnIo;
    private string? _note;
    private string _message = "";
    private bool _isBusy;
    private bool _active;
    private bool _refreshing;

    private readonly Func<Task> _driverChanged;

    /// <param name="driverChanged">Called after PawnIO is installed or removed, so readouts that depend on it (PBO) are re-read.</param>
    public SensorsPageViewModel(Func<EnginePipeClient?> client, PawnIoReport pawnIo, SensorReport sensors, Func<Task> driverChanged)
    {
        _client = client;
        _driverChanged = driverChanged;
        _pawnIo = pawnIo;
        Show(sensors);
        _tick.Tick += async (_, _) => await RefreshSensorsAsync();
    }

    public ObservableCollection<SensorView> Sensors { get; } = [];

    /// <summary>Whether the Overclocking page is showing; readouts only refresh while it is.</summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (!Set(ref _active, value))
                return;
            if (value)
                _tick.Start();
            else
                _tick.Stop();
        }
    }

    public bool Available => _pawnIo.Available;
    public bool Installed => _pawnIo.Installed;
    public bool CanInstall => Available && !Installed && !IsBusy;
    public bool CanUninstall => Installed && !IsBusy;

    public string Status => _pawnIo.Message;

    public string FaceitWarning => _pawnIo.FaceitWarning;

    public string? Note
    {
        get => _note;
        private set
        {
            if (Set(ref _note, value))
                OnPropertyChanged(nameof(HasNote));
        }
    }

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
                Changed();
        }
    }

    public void Stop() => _tick.Stop();

    /// <summary>Installs PawnIO. The window asks the user to accept the FACEIT warning first.</summary>
    public Task InstallAsync() => RunAsync(
        "Downloading, checking and installing PawnIO…",
        c => c.InstallPawnIoAsync(acceptFaceitWarning: true));

    public Task UninstallAsync() => RunAsync("Uninstalling PawnIO…", c => c.UninstallPawnIoAsync());

    private async Task RunAsync(string working, Func<EnginePipeClient, Task<PawnIoResult>> command)
    {
        if (_client() is not { } client)
        {
            Message = "Not connected to the service.";
            return;
        }

        IsBusy = true;
        Message = working;
        try
        {
            var result = await command(client);
            Log.Information("PawnIO command: {Succeeded} {Message}", result.Succeeded, result.Message);
            _pawnIo = result.Report;
            Message = result.Message;
            Show(await client.GetSensorsAsync());
            if (result.Succeeded)
                await _driverChanged();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "A PawnIO command failed");
            Message = $"Couldn't reach the service: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshSensorsAsync()
    {
        if (_refreshing || IsBusy || _client() is not { } client)
            return;
        _refreshing = true;
        try
        {
            Show(await client.GetSensorsAsync());
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Refreshing sensors failed");
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Show(SensorReport report)
    {
        Sensors.Clear();
        foreach (var sensor in report.Sensors)
            Sensors.Add(sensor);
        Note = report.Note;
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Available));
        OnPropertyChanged(nameof(Installed));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanUninstall));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(FaceitWarning));
    }
}
