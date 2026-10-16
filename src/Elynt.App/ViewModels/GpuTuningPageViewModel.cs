using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Elynt.Core.Ipc;
using Elynt.Core.Tuning;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>
/// The Overclocking page's GPU tuning: SDK-bounded sliders per GPU, auto tuning, reset, and the 15 s
/// keep-or-go-back countdown. The service owns the countdown; this only shows it and refreshes when it runs out.
/// </summary>
public sealed class GpuTuningPageViewModel : ObservableObject
{
    private readonly EnginePipeClient _client;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string? _notice;

    public GpuTuningPageViewModel(EnginePipeClient client, GpuTuningReport report)
    {
        _client = client;
        Show(report);
        _tick.Tick += async (_, _) => await TickAsync();
        _tick.Start();
    }

    public ObservableCollection<GpuTuningItemViewModel> Gpus { get; } = [];

    public string? Notice
    {
        get => _notice;
        private set
        {
            if (Set(ref _notice, value))
                OnPropertyChanged(nameof(HasNotice));
        }
    }

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    public void Stop() => _tick.Stop();

    internal async Task RunAsync(GpuTuningItemViewModel gpu, Func<EnginePipeClient, Task<TuningResult>> command)
    {
        gpu.IsBusy = true;
        try
        {
            var result = await command(_client);
            if (result.Gpu is not null)
                gpu.Update(result.Gpu);
            gpu.Message = result.Message;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "A GPU tuning command failed");
            gpu.Message = $"Couldn't reach the service: {ex.Message}";
        }
        finally
        {
            gpu.IsBusy = false;
        }
    }

    private void Show(GpuTuningReport report)
    {
        Notice = report.Notice;
        foreach (var view in report.Gpus)
        {
            if (Gpus.FirstOrDefault(g => g.DeviceId == view.DeviceId) is { } existing)
                existing.Update(view);
            else
                Gpus.Add(new GpuTuningItemViewModel(this, view));
        }
        foreach (var gone in Gpus.Where(g => report.Gpus.All(v => v.DeviceId != g.DeviceId)).ToList())
            Gpus.Remove(gone);
    }

    private async Task TickAsync()
    {
        var expired = false;
        foreach (var gpu in Gpus)
            expired |= gpu.Tick(DateTimeOffset.UtcNow);
        if (!expired)
            return;

        try
        {
            Show(await _client.GetGpuTuningAsync());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't refresh GPU tuning");
        }
    }
}

public sealed class GpuTuningItemViewModel : ObservableObject
{
    private readonly GpuTuningPageViewModel _page;
    private GpuTuningView _view;
    private bool _isBusy;
    private string? _message;
    private string _countdown = "";

    internal GpuTuningItemViewModel(GpuTuningPageViewModel page, GpuTuningView view)
    {
        _page = page;
        _view = view;
        Controls = view.Controls.Select(c => new TuningSliderViewModel(c)).ToList();
        Apply = new Command(() => _page.RunAsync(this, client => client.SetGpuTuningAsync(DeviceId, ChangedValues())), () => !IsBusy && ChangedValues().Count > 0);
        Keep = new Command(() => _page.RunAsync(this, client => client.ConfirmGpuTuningAsync(DeviceId)), () => !IsBusy && IsWaiting);
        GoBack = new Command(() => _page.RunAsync(this, client => client.RevertGpuTuningAsync(DeviceId)), () => !IsBusy && IsWaiting);
        Reset = new Command(() => _page.RunAsync(this, client => client.ResetGpuTuningAsync(DeviceId)), () => !IsBusy);
        AutoOverclock = new Command(() => _page.RunAsync(this, client => client.AutoTuneGpuAsync(DeviceId, AutoTuneKind.OverclockGpu)), () => !IsBusy && view.AutoTunes.Contains(AutoTuneKind.OverclockGpu));
        AutoUndervolt = new Command(() => _page.RunAsync(this, client => client.AutoTuneGpuAsync(DeviceId, AutoTuneKind.UndervoltGpu)), () => !IsBusy && view.AutoTunes.Contains(AutoTuneKind.UndervoltGpu));
        foreach (var slider in Controls)
            slider.PropertyChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
    }

    public string DeviceId => _view.DeviceId;

    public string Name => _view.Name;

    public IReadOnlyList<TuningSliderViewModel> Controls { get; }

    public bool AtDefaults => _view.AtDefaults;

    public string? Notice => _view.Notice;

    public bool IsWaiting => _view.RevertsAt is not null;

    /// <summary>"Keep these settings? Going back in 12 s".</summary>
    public string Countdown
    {
        get => _countdown;
        private set => Set(ref _countdown, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (Set(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? Message
    {
        get => _message;
        set => Set(ref _message, value);
    }

    public ICommand Apply { get; }
    public ICommand Keep { get; }
    public ICommand GoBack { get; }
    public ICommand Reset { get; }
    public ICommand AutoOverclock { get; }
    public ICommand AutoUndervolt { get; }

    internal void Update(GpuTuningView view)
    {
        _view = view;
        foreach (var slider in Controls)
        {
            if (view.Controls.FirstOrDefault(c => c.Id == slider.Id) is { } control)
                slider.Update(control);
        }
        OnPropertyChanged(nameof(AtDefaults));
        OnPropertyChanged(nameof(Notice));
        OnPropertyChanged(nameof(IsWaiting));
        Tick(DateTimeOffset.UtcNow);
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Updates the countdown. True when the service's deadline has passed and the page should refresh.</summary>
    internal bool Tick(DateTimeOffset now)
    {
        if (_view.RevertsAt is not { } deadline)
        {
            Countdown = "";
            return false;
        }
        var left = deadline - now;
        Countdown = left > TimeSpan.Zero
            ? $"Keep these settings? They go back in {Math.Ceiling(left.TotalSeconds):0} s."
            : "Going back…";
        return left <= TimeSpan.FromMilliseconds(-300);
    }

    private Dictionary<string, int> ChangedValues() =>
        Controls.Where(s => s.IsChanged).ToDictionary(s => s.Id, s => s.Value);
}

/// <summary>One slider, bounded by the SDK's range and snapped to its step.</summary>
public sealed class TuningSliderViewModel(TuningControl control) : ObservableObject
{
    private TuningControl _control = control;
    private int _value = control.Current;

    public string Id => _control.Id;
    public string Name => _control.Name;
    public string Unit => _control.Unit;
    public int Min => _control.Min;
    public int Max => _control.Max;
    public int Step => _control.Step;
    public string RangeText => $"{Min}–{Max} {Unit}, default {_control.Default}";

    public int Value
    {
        get => _value;
        set
        {
            var snapped = Math.Clamp(Min + (int)Math.Round((value - Min) / (double)Step) * Step, Min, Max);
            if (Set(ref _value, snapped))
            {
                OnPropertyChanged(nameof(ValueText));
                OnPropertyChanged(nameof(IsChanged));
            }
        }
    }

    public string ValueText => $"{Value} {Unit}";

    public bool IsChanged => Value != _control.Current;

    internal void Update(TuningControl control)
    {
        _control = control;
        _value = control.Current;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>A button command that runs one async action.</summary>
internal sealed class Command(Func<Task> run, Func<bool> canRun) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canRun();

    public async void Execute(object? parameter) => await run();
}
