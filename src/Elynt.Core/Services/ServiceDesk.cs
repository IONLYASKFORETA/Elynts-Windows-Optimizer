using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Services;

/// <summary>Lists services for one user and decides who may change each one and to what.</summary>
internal sealed class ServiceDesk(SystemAdapters system, ChangeJournal journal, string user)
{
    private static readonly IReadOnlyList<ServiceStart> NormalStarts = [ServiceStart.Automatic, ServiceStart.Manual];
    private static readonly IReadOnlyList<ServiceStart> AdvancedStarts = [ServiceStart.Automatic, ServiceStart.Manual, ServiceStart.Disabled];

    public IReadOnlyList<ServiceView> List() =>
        system.Services is null
            ? []
            : system.Services.List()
                .Select(Describe)
                .OrderBy(s => s.Access)
                .ThenBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

    /// <summary>
    /// Checks who may make this change, then hands it to the Engine's apply, which enforces the hard rules again and
    /// journals the whole configuration first.
    /// </summary>
    public ServiceResult Set(string name, ServiceStart start, ApplyOptions options, Func<TweakDefinition, ApplyOptions, CommandResult> apply)
    {
        if (system.Services is null)
            return new(false, "Services can't be changed here. Nothing was changed.", null);
        if (system.Services.Find(name) is not { } service)
            return new(false, $"There is no service called '{name}'.", null);

        var view = Describe(service);
        var refusal = view.Access switch
        {
            ServiceAccess.Protected => $"{view.DisplayName} is never changed, even in Advanced: {view.LockReason}.",
            _ when (options.Advanced ? view.AdvancedChoices : view.NormalChoices).Contains(start) => null,
            ServiceAccess.AdvancedOnly when !options.Advanced =>
                $"{view.DisplayName} isn't on the reviewed list of services that are safe to change. Turn on Advanced to change it.",
            _ when start == ServiceStart.Disabled && !options.Advanced => "Disabled is only available with Advanced on.",
            _ => $"{view.DisplayName} can't be set to start {start} here.",
        };
        if (refusal is not null)
            return new(false, $"{refusal} Nothing was changed.", view);

        var result = apply(ServiceChange.Define(service.Name, service.DisplayName, service.Kind, start), options);
        var message = result.Message switch
        {
            var m when m.StartsWith("Turned on.", StringComparison.Ordinal) =>
                $"{view.DisplayName} now starts {start}.{m["Turned on.".Length..]}",
            var m when m.StartsWith("Already on.", StringComparison.Ordinal) => $"{view.DisplayName} already starts {start}. Nothing was changed.",
            var m => m.Replace("Couldn't turn this on", "Couldn't change this service", StringComparison.Ordinal),
        };
        return new(result.Succeeded, message, Refresh(name));
    }

    /// <summary>Restates the Engine's undo of a service change in the Services page's terms.</summary>
    public ServiceResult Undo(string name, CommandResult result)
    {
        var view = Refresh(name);
        var message = result.Succeeded
            ? $"{view?.DisplayName ?? name} has its previous configuration back."
            : result.Message.Replace("There is no tweak called", "There is no service change called", StringComparison.Ordinal)
                .Replace("Switch the tweak on again", "Change the service again", StringComparison.Ordinal);
        return new(result.Succeeded, message, view);
    }

    private ServiceView? Refresh(string name)
    {
        try
        {
            return system.Services?.Find(name) is { } service ? Describe(service) : null;
        }
        catch
        {
            return null;
        }
    }

    public ServiceView Describe(ServiceInfo service)
    {
        var listed = SafeServices.Find(service.Name);
        var lockReason = HardRules.Violation(new ServiceTarget(service.Name, service.Config.Start), system.Services);
        var access = lockReason is not null ? ServiceAccess.Protected
            : listed is not null ? ServiceAccess.Listed
            : ServiceAccess.AdvancedOnly;

        return new ServiceView(
            service.Name,
            service.DisplayName,
            listed?.Explanation ?? service.Description,
            service.Kind,
            service.Config.Start,
            service.Config.DelayedAutoStart,
            service.Running,
            access,
            lockReason,
            access == ServiceAccess.Protected ? null : Recommend(service, listed),
            access == ServiceAccess.Listed ? NormalStarts : [],
            access == ServiceAccess.Protected ? [] : AdvancedStarts,
            journal.LatestUndoable(ServiceChange.TweakIdFor(service.Name), user) is not null);
    }

    /// <summary>Manual for a listed service whose hardware this PC doesn't have, while it is still set to start on its own.</summary>
    private ServiceRecommendation? Recommend(ServiceInfo service, SafeService? listed)
    {
        if (listed?.NotNeededWithout is not { } device || system.Devices is null)
            return null;
        if (service.Config.Start is not (ServiceStart.Automatic or ServiceStart.System or ServiceStart.Boot))
            return null;

        bool present;
        try
        {
            present = system.Devices.Has(device);
        }
        catch
        {
            return null;
        }
        if (present)
            return null;

        var missing = device switch
        {
            DeviceKind.Printer => "No printer was found",
            DeviceKind.BluetoothAdapter => "No Bluetooth adapter was found",
            _ => $"No {device} was found",
        };
        return new ServiceRecommendation(ServiceStart.Manual,
            $"{missing}, so this doesn't need to start with Windows. On Manual it still starts if something asks for it.");
    }
}
