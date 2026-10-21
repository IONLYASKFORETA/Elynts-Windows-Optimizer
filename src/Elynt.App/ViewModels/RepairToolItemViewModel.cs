using System.Collections.ObjectModel;
using Elynt.Core.Repair;

namespace Elynt.App.ViewModels;

/// <summary>One repair tool: its description and restart need up front, then live progress, the summary and the log.</summary>
public sealed class RepairToolItemViewModel(RepairToolView view) : ObservableObject
{
    private const int MaxOutputLines = 200;

    private bool _isRunning;
    private double? _percent;
    private string? _stepText;
    private RepairResult? _result;

    public RepairToolView View { get; } = view;

    public string Name => View.Name;

    public string RestartBadge => View.Restart switch
    {
        RestartNeed.None => "No restart",
        RestartNeed.Maybe => "May need a restart",
        _ => "Needs a restart",
    };

    public bool IsOptIn => View.Confirmation is not null;

    /// <summary>The latest output lines, newest last.</summary>
    public ObservableCollection<string> Output { get; } = [];

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (Set(ref _isRunning, value))
                OnPropertyChanged(nameof(CanRun));
        }
    }

    public bool CanRun => View.Available && !IsRunning;

    public string? StepText
    {
        get => _stepText;
        private set => Set(ref _stepText, value);
    }

    public double Percent => _percent ?? 0;

    public bool IsIndeterminate => IsRunning && _percent is null;

    public RepairResult? Result
    {
        get => _result;
        set
        {
            if (Set(ref _result, value))
            {
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(HasLog));
                OnPropertyChanged(nameof(HasFollowUp));
            }
        }
    }

    public bool HasResult => Result is not null;

    public bool HasLog => !string.IsNullOrEmpty(Result?.LogText);

    public bool HasFollowUp => Result?.FollowUpToolId is not null;

    public void Start()
    {
        Output.Clear();
        Result = null;
        _percent = null;
        StepText = "Starting…";
        IsRunning = true;
        OnProgressChanged();
    }

    public void Report(RepairProgress progress)
    {
        if (progress.Line is null)
        {
            _percent = null;
            StepText = $"Step {progress.Step} of {progress.StepCount}: {progress.StepName}";
        }
        else
        {
            if (progress.Percent is { } percent)
                _percent = percent;
            Output.Add(progress.Line);
            while (Output.Count > MaxOutputLines)
                Output.RemoveAt(0);
        }
        OnProgressChanged();
    }

    public void Finish(RepairResult result)
    {
        Result = result;
        IsRunning = false;
        StepText = null;
        OnProgressChanged();
    }

    private void OnProgressChanged()
    {
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(IsIndeterminate));
    }
}
