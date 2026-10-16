using Elynt.Core.Tweaks;

namespace Elynt.Core.Services;

public enum ServiceKind
{
    Service,
    /// <summary>A kernel or file-system driver.</summary>
    Driver,
}

/// <summary>
/// A service's full configuration, as QueryServiceConfig and QueryServiceConfig2 report it. Captured whole before any
/// change so undo puts every part of it back, not just the start type.
/// </summary>
/// <param name="ErrorControl">What Windows does if it fails to start: 0 ignore, 1 normal, 2 severe, 3 critical.</param>
/// <param name="Account">The account it runs as (LocalSystem, NT AUTHORITY\LocalService, ...); empty for drivers.</param>
public sealed record ServiceConfig(
    ServiceStart Start,
    bool DelayedAutoStart = false,
    int ErrorControl = 1,
    string BinaryPath = "",
    string Account = "",
    string LoadOrderGroup = "")
{
    /// <summary>Services and groups it needs running first.</summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];

    /// <summary>Starts on a trigger (a device arriving, a network event) even when set to Manual.</summary>
    public bool TriggerStart { get; init; }

    /// <summary>Every part equal, dependencies included (record equality compares the list by reference).</summary>
    public bool SameAs(ServiceConfig other) =>
        this with { Dependencies = [] } == other with { Dependencies = [] } &&
        Dependencies.SequenceEqual(other.Dependencies, StringComparer.OrdinalIgnoreCase);

    public string DescribeStart() => Start switch
    {
        ServiceStart.Automatic when DelayedAutoStart => "Automatic (delayed start)",
        _ => Start.ToString(),
    };
}

/// <summary>One installed service or driver.</summary>
public sealed record ServiceInfo(
    string Name,
    string DisplayName,
    string Description,
    ServiceKind Kind,
    bool Running,
    ServiceConfig Config);

/// <summary>
/// The Service Control Manager. The real adapter reads with QueryServiceConfig(2) and writes with
/// ChangeServiceConfig(2), never by editing the registry Start value.
/// </summary>
public interface IServiceManager
{
    /// <summary>Every installed service and driver.</summary>
    IReadOnlyList<ServiceInfo> List();

    /// <summary>One service or driver by name, or null when it isn't installed.</summary>
    ServiceInfo? Find(string name);

    /// <summary>Writes the whole configuration. Throws when Windows refuses (access denied, a protected service).</summary>
    void Configure(string name, ServiceConfig config);
}

/// <summary>Hardware that decides whether a service is worth keeping on Automatic.</summary>
public enum DeviceKind
{
    /// <summary>A real printer: not Microsoft Print to PDF, XPS, OneNote or Fax.</summary>
    Printer,
    BluetoothAdapter,
}

/// <summary>Detects whether the PC has a kind of device. Reads only.</summary>
public interface IDevicePresence
{
    bool Has(DeviceKind device);
}
