using System.Collections.ObjectModel;
using Elynt.Core;
using Elynt.Core.Ipc;
using Elynt.Core.Services;
using Elynt.Core.Tweaks;
using Serilog;

namespace Elynt.App.ViewModels;

/// <summary>
/// The Services page: every service and driver, reviewed ones editable, Advanced unlocking Disabled and the rest.
/// The service enforces every rule again; this only decides what to offer.
/// </summary>
/// <param name="client">The current connection to the service, or null while disconnected.</param>
/// <param name="confirmDriver">Asks before changing a driver, which can stop Windows from starting.</param>
public sealed class ServicesPageViewModel(Func<EnginePipeClient?> client, Func<ServiceItemViewModel, ServiceStart, bool> confirmDriver) : ObservableObject
{
    private IReadOnlyList<ServiceItemViewModel> _all = [];
    private bool _advanced;
    private string _filter = "";
    private string? _message;

    public ObservableCollection<ServiceItemViewModel> Services { get; } = [];

    /// <summary>Unlocks Disabled and services off the reviewed list. Protected services stay locked.</summary>
    public bool Advanced
    {
        get => _advanced;
        set
        {
            if (!Set(ref _advanced, value))
                return;
            foreach (var item in _all)
                item.Advanced = value;
            Show();
        }
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value))
                Show();
        }
    }

    public string? Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public string Summary =>
        $"{_all.Count(s => s.View.Access == ServiceAccess.Listed)} reviewed services can be changed. " +
        $"{_all.Count(s => s.View.Recommendation is not null)} recommendations for this PC.";

    public async Task LoadAsync()
    {
        if (client() is not { } connection)
            return;
        try
        {
            var views = await connection.ListServicesAsync();
            _all = views.Select(v => new ServiceItemViewModel(v, Advanced)).ToList();
            Message = null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't list services");
            _all = [];
            Message = $"Couldn't list services: {ex.Message}";
        }
        OnPropertyChanged(nameof(Summary));
        Show();
    }

    public async Task SetAsync(ServiceItemViewModel item)
    {
        if (client() is not { } connection || item.SelectedChoice is not { } start || item.IsBusy)
            return;
        if (item.View.Kind == ServiceKind.Driver && !confirmDriver(item, start))
            return;

        item.IsBusy = true;
        try
        {
            var result = await connection.SetServiceStartAsync(item.View.Name, start, new ApplyOptions(Advanced: Advanced));
            Log.Information("Service {Service} to {Start}: {Succeeded} {Message}", item.View.Name, start, result.Succeeded, result.Message);
            Finish(item, result);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Changing {Service} failed", item.View.Name);
            Message = $"Couldn't reach the service: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    public async Task UndoAsync(ServiceItemViewModel item)
    {
        if (client() is not { } connection || item.IsBusy)
            return;
        item.IsBusy = true;
        try
        {
            Finish(item, await connection.UndoServiceChangeAsync(item.View.Name));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Undoing {Service} failed", item.View.Name);
            Message = $"Couldn't reach the service: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private void Finish(ServiceItemViewModel item, ServiceResult result)
    {
        Message = result.Message;
        if (result.Service is { } view)
            item.Update(view);
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Recommendations first, then reviewed services; the rest only with Advanced on or when searched for.</summary>
    private void Show()
    {
        Services.Clear();
        var search = Filter.Trim();
        foreach (var item in _all
                     .Where(s => search.Length > 0
                         ? s.View.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                           s.View.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                         : Advanced || s.View.Access == ServiceAccess.Listed)
                     .OrderBy(s => s.View.Recommendation is null)
                     .ThenBy(s => s.View.Access)
                     .ThenBy(s => s.View.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            Services.Add(item);
    }
}

/// <summary>One row of the Services page.</summary>
public sealed class ServiceItemViewModel : ObservableObject
{
    private ServiceView _view;
    private bool _advanced;
    private bool _isBusy;
    private ServiceStart? _selectedChoice;

    public ServiceItemViewModel(ServiceView view, bool advanced)
    {
        _view = view;
        _advanced = advanced;
        _selectedChoice = Pick();
    }

    public ServiceView View => _view;

    public string Title => _view.DisplayName == _view.Name ? _view.Name : $"{_view.DisplayName} ({_view.Name})";

    public string StartText =>
        $"{(_view.Start == ServiceStart.Automatic && _view.DelayedAutoStart ? "Automatic (delayed start)" : _view.Start.ToString())}" +
        $"{(_view.Running ? ", running" : "")}{(_view.Kind == ServiceKind.Driver ? " · driver" : "")}";

    public string Badge => _view.Access switch
    {
        ServiceAccess.Listed => "Reviewed",
        ServiceAccess.AdvancedOnly => "Advanced only",
        _ => "Protected",
    };

    public string? RecommendationText => _view.Recommendation is { } r ? $"Recommended: {r.Start}. {r.Reason}" : null;

    public bool HasRecommendation => _view.Recommendation is not null;

    public bool IsProtected => _view.Access == ServiceAccess.Protected;

    public IReadOnlyList<ServiceStart> Choices => Advanced ? _view.AdvancedChoices : _view.NormalChoices;

    public bool CanChange => Choices.Count > 0 && !IsBusy;

    public bool CanUndo => _view.CanUndo && !IsBusy;

    public ServiceStart? SelectedChoice
    {
        get => _selectedChoice;
        set => Set(ref _selectedChoice, value);
    }

    public bool Advanced
    {
        get => _advanced;
        set
        {
            if (!Set(ref _advanced, value))
                return;
            OnPropertyChanged(nameof(Choices));
            OnPropertyChanged(nameof(CanChange));
            SelectedChoice = Pick();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!Set(ref _isBusy, value))
                return;
            OnPropertyChanged(nameof(CanChange));
            OnPropertyChanged(nameof(CanUndo));
        }
    }

    public void Update(ServiceView view)
    {
        _view = view;
        SelectedChoice = Pick();
        foreach (var name in new[]
                 {
                     nameof(View), nameof(Title), nameof(StartText), nameof(Badge), nameof(RecommendationText),
                     nameof(HasRecommendation), nameof(IsProtected), nameof(Choices), nameof(CanChange), nameof(CanUndo),
                 })
            OnPropertyChanged(name);
    }

    /// <summary>The recommendation when there is one, otherwise the current start type when it can be picked.</summary>
    private ServiceStart? Pick() =>
        _view.Recommendation is { } r && Choices.Contains(r.Start) ? r.Start
        : Choices.Contains(_view.Start) ? _view.Start
        : Choices.FirstOrDefault() is var first && Choices.Count > 0 ? first : null;
}
