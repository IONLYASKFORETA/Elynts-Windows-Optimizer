using Elynt.Core.Journal;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Services;

/// <summary>
/// A start-type change to one service, run and journaled like a tweak under its own id ("service:Spooler"), so it
/// gets the same hard rules, safety net, write-ahead journal and Undo history as every other change.
/// </summary>
public static class ServiceChange
{
    public const string IdPrefix = "service:";

    public static string TweakIdFor(string serviceName) => IdPrefix + serviceName;

    /// <summary>The tweak that sets <paramref name="serviceName"/> to <paramref name="start"/>.</summary>
    public static TweakDefinition Define(string serviceName, string displayName, ServiceKind kind, ServiceStart start) => new(
        TweakIdFor(serviceName),
        Sections.StartupAndServices,
        $"{displayName}: start type",
        $"Sets {displayName} to start {start}.",
        "Chosen by you on the Services page.",
        [],
        EvidenceTier.Situational,
        "Puts back the service's whole previous configuration: start type, delayed start, error control, path, account, group and dependencies.",
        [new ServiceStartStep(serviceName, start)])
    {
        Risks = kind == ServiceKind.Driver ? RiskFlags.CanStopBoot : RiskFlags.None,
    };

    /// <summary>The tweak behind a journaled service change, or null when the id isn't a service change.</summary>
    internal static TweakDefinition? Resolve(string tweakId, JournalEntry? entry, SystemAdapters system)
    {
        if (!tweakId.StartsWith(IdPrefix, StringComparison.Ordinal))
            return null;
        var name = tweakId[IdPrefix.Length..];
        var start = entry?.Steps.OfType<ServiceConfigRecord>().FirstOrDefault()?.After.Start ?? ServiceStart.Manual;

        ServiceInfo? service = null;
        try
        {
            service = system.Services?.Find(name);
        }
        catch
        {
            // Named by its service name instead.
        }
        return Define(name, service?.DisplayName ?? name, service?.Kind ?? ServiceKind.Service, start);
    }
}

/// <summary>Sets one service's start type, leaving the rest of its configuration as it is.</summary>
public sealed record ServiceStartStep(string ServiceName, ServiceStart Start) : TweakStep
{
    public override ChangeTarget Target => new ServiceTarget(ServiceName, Start);

    public override string DescribeChange() => $"Set the service {ServiceName} to start {Start}";

    public override bool IsApplied(SystemAdapters system) => Manager(system).Find(ServiceName)?.Config.Start == Start;

    public override StepRecord Capture(SystemAdapters system)
    {
        var before = Current(system);
        return new ServiceConfigRecord(ServiceName, before, Desired(before));
    }

    public override void Apply(SystemAdapters system) => Manager(system).Configure(ServiceName, Desired(Current(system)));

    private ServiceConfig Desired(ServiceConfig current) => current with { Start = Start };

    private ServiceConfig Current(SystemAdapters system) =>
        (Manager(system).Find(ServiceName) ?? throw new InvalidOperationException($"The service {ServiceName} isn't installed.")).Config;

    internal static IServiceManager Manager(SystemAdapters system) =>
        system.Services ?? throw new InvalidOperationException("Services can't be changed here.");
}

/// <summary>A service's whole configuration before the app changed it, and what the app wrote.</summary>
public sealed record ServiceConfigRecord(string ServiceName, ServiceConfig Before, ServiceConfig After) : StepRecord
{
    /// <summary>A service runs for the whole PC.</summary>
    public override bool AffectsEveryAccount() => true;

    public override string? FindConflict(SystemAdapters system)
    {
        var now = ServiceStartStep.Manager(system).Find(ServiceName);
        if (now is null)
            return $"The service {ServiceName} is no longer installed.";
        if (now.Config.Start == After.Start || now.Config.Start == Before.Start)
            return null;
        return $"Something else changed {ServiceName} since the app set it to {After.Start}. It now starts {now.Config.Start}.";
    }

    public override void Revert(SystemAdapters system)
    {
        // Nothing to write when it already has its old configuration (the change never went through).
        var services = ServiceStartStep.Manager(system);
        if (services.Find(ServiceName)?.Config.SameAs(Before) != true)
            services.Configure(ServiceName, Before);
    }
}
