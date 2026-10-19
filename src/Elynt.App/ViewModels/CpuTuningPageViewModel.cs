using System.Windows.Input;
using System.Windows.Threading;
using Elynt.Core.Ipc;
using Elynt.Core.Tuning.Cpu;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>
/// The Overclocking page's CPU tuning: Curve Optimizer and power-limit sliders whose ranges already carry the
/// service's limits, the 60 s keep-or-go-back countdown, the stress test and auto-apply. The service enforces every
/// limit; this only shows them.
/// </summary>
public sealed class CpuTuningPageViewModel : ObservableObject
{
    private readonly EnginePipeClient _client;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CpuTuningReport _report;
    private IReadOnlyList<TuningSliderViewModel> _controls = [];
    private bool _advanced;
    private bool _unofficial;
    private bool _isBusy;
    private string? _message;
    private string _countdown = "";

    public CpuTuningPageViewModel(EnginePipeClient client, CpuTuningReport report)
    {
        _client = client;
        _report = report;
        Show(report);
        Apply = new Command(() => RunAsync(c => c.SetCpuTuningAsync(ChangedTune(), Options)), () => !IsBusy && Available && Controls.Any(s => s.IsChanged));
        UseIntelDefaults = new Command(() => RunAsync(c => c.SetCpuTuningAsync(_report.IntelDefaults!, Options)), () => !IsBusy && _report.IntelDefaults is not null);
        Keep = new Command(() => RunAsync(c => c.ConfirmCpuTuningAsync()), () => !IsBusy && IsWaiting);
        GoBack = new Command(() => RunAsync(c => c.RevertCpuTuningAsync()), () => !IsBusy && IsWaiting);
        Reset = new Command(() => RunAsync(c => c.ResetCpuTuningAsync()), () => !IsBusy && Available);
        StressTest = new Command(StressTestAsync, () => !IsBusy && _report.Kept is not null && !IsWaiting);
        ToggleAutoApply = new Command(() => RunAsync(c => c.SetCpuAutoApplyAsync(!_report.AutoApply)), () => !IsBusy && (_report.AutoApply || _report.StressTestPassed));
        _tick.Tick += async (_, _) => await TickAsync();
        _tick.Start();
    }

    public bool Available => _report.Available;
    public string CpuName => _report.CpuName;
    public string? Notice => _report.Notice;

    /// <summary>The page's explainer, including the floors the service holds limits above (CpuTuning's constants).</summary>
    public string Explainer => ExplainerText;

    private static readonly string ExplainerText =
        "Curve Optimizer only goes negative and power limits only go down: never below " +
        $"{CpuTuning.IntelFloorWatts} W for Intel PL1 and PL2, and never below {(int)(CpuTuning.AmdFloorShare * 100)}% of your BIOS value for AMD PPT, TDC and EDC. " +
        "Every change goes back after 60 seconds unless you keep it, and a restart always brings back your BIOS settings.";

    public bool HasNotice => !string.IsNullOrEmpty(Notice);
    public IReadOnlyList<string> Warnings => _report.Warnings;
    public bool IsWaiting => _report.RevertsAt is not null;
    public bool HasIntelDefaults => _report.IntelDefaults is not null;
    public bool OffersUnofficial => _report.CurveOptimizerUnofficial;

    public string Status => _report switch
    {
        { Disabled: true } => $"Turned off after {_report.Strikes} crashes or hardware errors. Run the stress test again to re-enable.",
        { AutoApply: true } => "Kept tune re-applied 90 seconds after each sign-in and resume." + Strikes,
        { StressTestPassed: true } => "Kept tune passed the stress test. Turn on auto-apply to have it come back after restarts." + Strikes,
        { Kept: not null } => "Kept until the PC restarts or sleeps. Pass the stress test to unlock auto-apply." + Strikes,
        _ => "On BIOS settings.",
    };

    private string Strikes => _report.Strikes > 0 ? $" Strikes: {_report.Strikes} of 2." : "";

    public string AutoApplyText => _report.AutoApply ? "Turn off auto-apply" : "Turn on auto-apply";

    public IReadOnlyList<TuningSliderViewModel> Controls
    {
        get => _controls;
        private set => Set(ref _controls, value);
    }

    /// <summary>Advanced lets Curve Optimizer go to -30 instead of -15.</summary>
    public bool Advanced
    {
        get => _advanced;
        set
        {
            if (Set(ref _advanced, value))
                _ = RefreshAsync();
        }
    }

    /// <summary>The user accepts that Curve Optimizer is unofficial on this CPU (5800X3D and friends).</summary>
    public bool Unofficial
    {
        get => _unofficial;
        set => Set(ref _unofficial, value);
    }

    public string Countdown
    {
        get => _countdown;
        private set => Set(ref _countdown, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public ICommand Apply { get; }
    public ICommand UseIntelDefaults { get; }
    public ICommand Keep { get; }
    public ICommand GoBack { get; }
    public ICommand Reset { get; }
    public ICommand StressTest { get; }
    public ICommand ToggleAutoApply { get; }

    public void Stop() => _tick.Stop();

    private CpuTuningOptions Options => new(Advanced, Unofficial);

    private async Task StressTestAsync()
    {
        Message = "Stress testing for 5 minutes. The PC will be busy; don't start a game until it finishes.";
        await RunAsync(c => c.RunCpuStressTestAsync());
    }

    private async Task RunAsync(Func<EnginePipeClient, Task<CpuTuningResult>> command)
    {
        IsBusy = true;
        try
        {
            var result = await command(_client);
            Show(result.Cpu);
            Message = result.Message;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "A CPU tuning command failed");
            Message = $"Couldn't reach the service: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            Show(await _client.GetCpuTuningAsync(Advanced));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't refresh CPU tuning");
        }
    }

    private void Show(CpuTuningReport report)
    {
        _report = report;
        Controls = report.Controls.Select(c => new TuningSliderViewModel(c)).ToList();
        foreach (var slider in Controls)
            slider.PropertyChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
        OnPropertyChanged(string.Empty);
        Tick(DateTimeOffset.UtcNow);
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>The whole tune as the sliders show it; a slider left at its BIOS value is left out.</summary>
    private CpuTune ChangedTune()
    {
        int? Value(string id) => Controls.FirstOrDefault(s => s.Id == id) is { } slider && slider.Value != Default(slider) ? slider.Value : null;
        return new CpuTune(
            Value(CpuControls.CurveOptimizer),
            Value(CpuControls.Ppt),
            Value(CpuControls.Tdc),
            Value(CpuControls.Edc),
            Value(CpuControls.Pl1),
            Value(CpuControls.Pl2));
    }

    private int Default(TuningSliderViewModel slider) =>
        _report.Controls.First(c => c.Id == slider.Id).Default;

    private bool Tick(DateTimeOffset now)
    {
        if (_report.RevertsAt is not { } deadline)
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

    private async Task TickAsync()
    {
        if (Tick(DateTimeOffset.UtcNow))
            await RefreshAsync();
    }
}
