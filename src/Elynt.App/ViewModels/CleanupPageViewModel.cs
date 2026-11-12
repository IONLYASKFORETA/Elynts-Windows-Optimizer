using System.Collections.ObjectModel;
using Elynt.Core.Cleanup;
using Elynt.Core.Ipc;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>
/// The Cleanup page: scan first, show sizes per category, tick what to clean. Scans and cleans can take minutes, so
/// they are long jobs: the client sends them over its long-job connection and switches keep working meanwhile.
/// </summary>
/// <param name="client">The current connection to the service, or null while disconnected.</param>
public sealed class CleanupPageViewModel(Func<EnginePipeClient?> client) : ObservableObject
{
    private const string NotConnected = "Not connected to the service, so nothing was scanned or cleaned.";
    private bool _isBusy;
    private bool _hasScanned;
    private string _status = "Scan to see how much space each category takes. Nothing is deleted until you press Clean.";
    private bool _weeklyEnabled;
    private bool _scheduleLoaded;

    /// <summary>The separate confirmation destructive categories need, given the warnings. "No" must be the default.</summary>
    public Func<string, bool> ConfirmDestructive { get; set; } = _ => false;

    public ObservableCollection<CleanupCategoryItemViewModel> Categories { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanScan));
                OnPropertyChanged(nameof(CanClean));
            }
        }
    }

    public bool CanScan => !IsBusy;

    /// <summary>The Scan button's words: "Scan again" once the list shows a scan's sizes, even after cleaning.</summary>
    public string ScanText => Categories.Count > 0 ? "Scan again" : "Scan";

    /// <summary>Cleaning is only offered after a scan has shown the sizes.</summary>
    public bool CanClean => !IsBusy && _hasScanned;

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>Weekly cleaning of safe categories only. Off by default.</summary>
    public bool WeeklyEnabled
    {
        get => _weeklyEnabled;
        set
        {
            if (Set(ref _weeklyEnabled, value) && _scheduleLoaded)
                _ = SaveWeeklyAsync(value);
        }
    }

    public async Task OnShownAsync()
    {
        if (_scheduleLoaded || client() is not { } connection)
            return;
        try
        {
            var schedule = await connection.GetCleanupScheduleAsync();
            _weeklyEnabled = schedule.WeeklyEnabled;
            OnPropertyChanged(nameof(WeeklyEnabled));
            _scheduleLoaded = true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't read the weekly cleaning setting");
        }
    }

    public async Task ScanAsync()
    {
        if (IsBusy)
            return;
        if (client() is not { } connection)
        {
            Status = NotConnected;
            return;
        }
        IsBusy = true;
        Status = "Scanning…";
        try
        {
            var scan = await connection.ScanCleanupAsync(new Progress<CleanupProgress>(p =>
                Status = $"Scanning {p.CategoryName} ({p.Index} of {p.Count})…"));
            Categories.Clear();
            foreach (var category in scan.Categories)
            {
                var item = new CleanupCategoryItemViewModel(category);
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(CleanupCategoryItemViewModel.IsTicked))
                        OnPropertyChanged(nameof(TickedSummary));
                };
                Categories.Add(item);
            }
            _hasScanned = true;
            Status = "Scan done. Safe caches are ticked; shader caches and the extras that can't be undone are up to you.";
            OnPropertyChanged(nameof(TickedSummary));
            OnPropertyChanged(nameof(ScanText));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cleanup scan failed");
            Status = ex is ServiceConnectionLostException
                ? "The connection to the service was lost during the scan. A scan changes nothing; close the app and open it again, then scan once more."
                : $"Couldn't scan: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string TickedSummary
    {
        get
        {
            var ticked = Categories.Where(c => c.IsTicked).ToList();
            if (ticked.Count == 0)
                return "Nothing ticked.";
            var bytes = ticked.Sum(c => c.View.SizeBytes ?? 0);
            var unknown = ticked.Any(c => c.View.SizeBytes is null) ? " plus what ResetBase frees" : "";
            return $"{ticked.Count} ticked: about {CleanupCategoryItemViewModel.Bytes(bytes)}{unknown}.";
        }
    }

    public async Task CleanAsync()
    {
        if (!CanClean)
            return;
        var ticked = Categories.Where(c => c.IsTicked).ToList();
        if (ticked.Count == 0)
        {
            Status = "Tick at least one category first.";
            return;
        }

        // The service refuses destructive categories without this flag; it is only set after the user confirms.
        var destructive = ticked.Where(c => c.View.NeedsConfirmation).ToList();
        var confirmed = false;
        if (destructive.Count > 0)
        {
            var warnings = string.Join("\n\n", destructive.Select(c => $"{c.Name}: {c.View.Warning}"));
            confirmed = ConfirmDestructive(warnings);
            if (!confirmed)
            {
                Status = "Not cleaned. Untick the categories that can't be undone, or confirm them.";
                return;
            }
        }

        if (client() is not { } connection)
        {
            Status = NotConnected;
            return;
        }
        IsBusy = true;
        Status = "Cleaning…";
        try
        {
            var report = await connection.CleanAsync(ticked.Select(c => c.View.Id).ToList(), confirmed,
                new Progress<CleanupProgress>(p => Status = $"Cleaning {p.CategoryName} ({p.Index} of {p.Count})…"));
            Log.Information("Cleanup: {Succeeded} {Message}", report.Succeeded, report.Message);
            foreach (var outcome in report.Categories)
                Categories.FirstOrDefault(c => c.View.Id == outcome.CategoryId)?.ShowOutcome(outcome);
            Status = report.Message;
            _hasScanned = false;
            OnPropertyChanged(nameof(CanClean));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cleanup failed");
            Status = ex is ServiceConnectionLostException ? ex.Message : $"Couldn't clean: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveWeeklyAsync(bool enabled)
    {
        void PutBack()
        {
            _weeklyEnabled = !enabled;
            OnPropertyChanged(nameof(WeeklyEnabled));
        }

        if (client() is not { } connection)
        {
            Status = "Not connected to the service, so weekly cleaning wasn't changed.";
            PutBack();
            return;
        }
        try
        {
            var schedule = await connection.SetWeeklyCleanupAsync(enabled);
            Status = schedule.WeeklyEnabled
                ? "Weekly cleaning is on: once a week the service cleans the safe categories only."
                : "Weekly cleaning is off.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Couldn't change weekly cleaning");
            Status = $"Couldn't change weekly cleaning: {ex.Message}";
            PutBack();
        }
    }
}

/// <summary>One cleanup category after a scan: a tick, its size, and any warning or reason it's skipped.</summary>
public sealed class CleanupCategoryItemViewModel(CleanupCategoryView view) : ObservableObject
{
    private bool _isTicked = view.Ticked;
    private string? _result;

    public CleanupCategoryView View { get; } = view;

    public string Name => View.Name;

    public string WhatItDoes => View.WhatItDoes;

    public bool IsTicked
    {
        get => _isTicked;
        set => Set(ref _isTicked, value);
    }

    /// <summary>A category whose browser is open can't be ticked until it's closed and scanned again.</summary>
    public bool CanTick => View.SkippedReason is null;

    public string SizeText => View.SizeBytes is { } bytes ? Bytes(bytes) : "Size unknown until it runs";

    public string KindText => View.Kind switch
    {
        CleanupKind.Safe => "Safe",
        CleanupKind.ShaderCache => "Causes stutter at first",
        _ => "Can't be undone",
    };

    /// <summary>The stutter or destructive warning, or why it's skipped, for the list.</summary>
    public string? Note => View.SkippedReason ?? View.Warning;

    public bool HasNote => Note is not null;

    public string LocationsText => View.Locations.Count == 0 ? "Nothing found." : string.Join(Environment.NewLine, View.Locations);

    public string? Result
    {
        get => _result;
        private set
        {
            if (Set(ref _result, value))
                OnPropertyChanged(nameof(HasResult));
        }
    }

    public bool HasResult => Result is not null;

    public void ShowOutcome(CleanupOutcome outcome)
    {
        Result = outcome.Message;
        IsTicked = false;
    }

    public static string Bytes(long bytes) => CleanupSizes.Format(bytes);
}
