using System.ComponentModel;
using System.Windows.Threading;
using Elynt.Core.Overview;
using Elynt.Core.Sensors;
using Elynt.Core.Tweaks;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>
/// The Home page: the PC overview from the service, live CPU and GPU temperatures while Home is on screen, what is
/// waiting (restarts, driver updates, drift), and a card per section. Every action opens the page that deals with it.
/// </summary>
public sealed class HomeViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(3) };
    private PcOverview? _overview;
    private SensorReport? _sensors;
    private DriftViewModel? _watchedDrift;
    private bool _active;
    private bool _refreshing;
    private bool _readingTemperatures;

    public HomeViewModel(MainViewModel main)
    {
        _main = main;
        _tick.Tick += async (_, _) => await RefreshTemperaturesAsync();
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsConnected))
                OnPropertyChanged(nameof(IsConnected));
        };
    }

    /// <summary>
    /// Whether Home is on screen. While it is, the temperatures refresh every few seconds; becoming active also
    /// re-reads the overview, so the counts include what changed on other pages.
    /// </summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (!Set(ref _active, value))
                return;
            if (value)
            {
                _tick.Start();
                _ = RefreshAsync();
            }
            else
            {
                _tick.Stop();
            }
        }
    }

    public bool IsConnected => _main.IsConnected;

    // ---- This PC ----

    /// <summary>The card's heading: the CPU, which says most about the PC.</summary>
    public string Heading => _overview is null ? "Reading this PC…" : _overview.Hardware.Cpu ?? "This PC";

    /// <summary>The board, graphics, memory and Windows, one line each. Parts that couldn't be read are left out.</summary>
    public IReadOnlyList<Spec> Specs
    {
        get
        {
            if (_overview is not { Hardware: var pc, Windows: var windows })
                return [];
            var specs = new List<Spec>();
            if (pc.Board is { } board)
                specs.Add(new("Board", board));
            if (pc.Gpus.Count > 0)
                specs.Add(new(pc.Gpus.Count == 1 ? "Graphics" : "GPUs", string.Join(", ", pc.Gpus)));
            if (pc.MemoryMb is { } mb)
                specs.Add(new("Memory", $"{mb / 1024.0:0.#} GB"));
            specs.Add(new("Windows", WindowsText(windows)));
            return specs;
        }
    }

    private static string WindowsText(WindowsRelease windows)
    {
        var edition = windows.Edition == WindowsEdition.Unknown ? "Windows 11" : $"Windows 11 {windows.Edition}";
        var version = windows.Version is { } name ? $" {name}" : "";
        return $"{edition}{version}, build {windows.Build}";
    }

    /// <summary>
    /// The CPU's and each GPU's temperature. Without PawnIO the CPU reading, if any, is Windows' thermal zone, which
    /// is often the motherboard's, so it is called "CPU area".
    /// </summary>
    public IReadOnlyList<Spec> Temperatures
    {
        get
        {
            if (_sensors is null)
                return [];
            var readings = new List<Spec>();
            if (_sensors.Sensors.FirstOrDefault(s => s.Id == SensorIds.CpuTemperature) is { } cpu)
                readings.Add(new(_sensors.FromPawnIo ? "CPU" : "CPU area", Degrees(cpu.Value)));
            // A card's own temperature is "<card>.temperature"; other readings (hotspot, memory) aren't another GPU.
            var gpus = _sensors.Sensors
                .Where(s => s.Kind == SensorKind.Temperature && s.Id != SensorIds.CpuTemperature && s.Id.EndsWith(".temperature", StringComparison.Ordinal))
                .ToList();
            for (var i = 0; i < gpus.Count; i++)
                readings.Add(new(gpus.Count == 1 ? "GPU" : $"GPU {i + 1}", Degrees(gpus[i].Value)));
            return readings;
        }
    }

    private static string Degrees(double celsius) => $"{celsius:0} °C";

    public bool HasTemperatures => Temperatures.Count > 0;

    /// <summary>Without PawnIO: how to get the CPU's own temperature, worded as on the Overclocking page.</summary>
    public string? TemperatureNote => _sensors?.Note;

    public bool HasTemperatureNote => !string.IsNullOrEmpty(TemperatureNote);

    public bool HasNoTemperatures => !HasTemperatures && !HasTemperatureNote;

    // ---- Stat cards ----

    public int TweaksOn => _overview?.TweaksOn ?? 0;

    public string TweaksOnCaption => $"tweaks on, of {_overview?.TweaksApplicable ?? 0} that fit this PC";

    public int RestartsWaiting => _overview?.PendingReboots ?? 0;

    public string RestartsCaption => RestartsWaiting == 1 ? "change waiting for a restart" : "changes waiting for a restart";

    private bool DriversChecked => _main.Drivers?.Checked == true;

    public int DriverUpdates => _main.Drivers?.UpdatesFound ?? 0;

    /// <summary>The number of driver updates found at the last check, or a dash before the first check.</summary>
    public string DriverUpdatesText => DriversChecked ? DriverUpdates.ToString() : "–";

    public string DriverUpdatesCaption =>
        !DriversChecked ? "drivers not checked yet" :
        DriverUpdates == 1 ? "driver update found at the last check" : "driver updates found at the last check";

    /// <summary>Changes Windows undid, from the drift check at sign-in.</summary>
    public int DriftFound => _main.Drift?.Items.Count ?? 0;

    public string DriftCaption => DriftFound == 1 ? "change Windows undid" : "changes Windows undid";

    // ---- Waiting ----

    /// <summary>What needs the user, each opening the page that deals with it.</summary>
    public IReadOnlyList<WaitingItem> Waiting
    {
        get
        {
            var waiting = new List<WaitingItem>();
            if (RestartsWaiting > 0)
                waiting.Add(new(RestartsWaiting == 1
                    ? "1 change takes effect after a restart"
                    : $"{RestartsWaiting} changes take effect after a restart", MainViewModel.UndoHistory));
            if (DriverUpdates > 0)
                waiting.Add(new(DriverUpdates == 1 ? "A newer driver is available" : $"{DriverUpdates} newer drivers are available", Sections.Drivers));
            if (DriftFound > 0)
                waiting.Add(new(DriftFound == 1 ? "Windows undid 1 change" : $"Windows undid {DriftFound} changes", MainViewModel.UndoHistory));
            return waiting;
        }
    }

    public bool HasWaiting => Waiting.Count > 0;

    public bool NothingWaiting => !HasWaiting;

    // ---- Sections ----

    /// <summary>A card per section with how much of it is on. Empty until the overview arrives.</summary>
    public IReadOnlyList<SectionCard> SectionCards =>
        _overview?.Sections.Select(s => new SectionCard(s.Section, s.On, s.Total)).ToList() ?? [];

    // ---- Actions ----

    public void Open(string page) => _main.SelectedPage = page;

    /// <summary>Opens Cleanup and starts its scan, as its own Scan button does.</summary>
    public async Task CleanUpAsync()
    {
        Open(Sections.Cleanup);
        await _main.Cleanup.ScanAsync();
    }

    /// <summary>Opens Drivers and checks every driver, as each one's own check button does.</summary>
    public async Task CheckDriversAsync()
    {
        Open(Sections.Drivers);
        if (_main.Drivers is { } drivers)
            await drivers.CheckAllAsync();
    }

    /// <summary>Forgets the service's figures when the connection drops, so nothing old shows as current.</summary>
    public void Clear()
    {
        _overview = null;
        _sensors = null;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Reads the overview and the temperatures, and re-reads the driver and drift results.</summary>
    public async Task RefreshAsync()
    {
        WatchDrift();
        if (_refreshing || _main.Client is not { } client)
        {
            OnPropertyChanged(string.Empty);
            return;
        }
        _refreshing = true;
        try
        {
            _overview = await client.GetPcOverviewAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't read the PC overview");
        }
        finally
        {
            _refreshing = false;
        }
        OnPropertyChanged(string.Empty);
        await RefreshTemperaturesAsync();
    }

    private async Task RefreshTemperaturesAsync()
    {
        if (_readingTemperatures || _main.Client is not { } client)
            return;
        _readingTemperatures = true;
        try
        {
            _sensors = await client.GetSensorsAsync();
            OnPropertyChanged(nameof(Temperatures));
            OnPropertyChanged(nameof(HasTemperatures));
            OnPropertyChanged(nameof(TemperatureNote));
            OnPropertyChanged(nameof(HasTemperatureNote));
            OnPropertyChanged(nameof(HasNoTemperatures));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Refreshing the Home page's temperatures failed");
        }
        finally
        {
            _readingTemperatures = false;
        }
    }

    /// <summary>The drift check runs after the window connects, while Home is showing, so its result is followed live.</summary>
    private void WatchDrift()
    {
        if (_main.Drift is not { } drift || drift == _watchedDrift)
            return;
        _watchedDrift = drift;
        drift.PropertyChanged += OnDriftChanged;
    }

    private void OnDriftChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DriftViewModel.HasDrift))
            return;
        OnPropertyChanged(nameof(DriftFound));
        OnPropertyChanged(nameof(DriftCaption));
        OnPropertyChanged(nameof(Waiting));
        OnPropertyChanged(nameof(HasWaiting));
        OnPropertyChanged(nameof(NothingWaiting));
    }

    /// <summary>One line of the This PC card: a part and what it is, or a temperature.</summary>
    public sealed record Spec(string Label, string Value);

    /// <summary>Something waiting for the user, and the page that deals with it.</summary>
    public sealed record WaitingItem(string Text, string Page);

    /// <summary>A section's card: its on count out of those that fit this PC, and a meter. A section of tools has no count.</summary>
    public sealed record SectionCard(string Section, int On, int Total)
    {
        public string Status => Total == 0 ? "Tools" : $"{On} of {Total} on";

        public double Fraction => Total == 0 ? 0 : (double)On / Total;
    }
}
