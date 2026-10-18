using Elynt.Core.Services;
using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests.Services;

public sealed class FakeServiceManager : IServiceManager
{
    private readonly Dictionary<string, ServiceInfo> _services = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Services Windows refuses to reconfigure (access denied, protected process).</summary>
    public HashSet<string> Denied { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int Writes { get; private set; }

    public FakeServiceManager Add(string name, ServiceStart start, ServiceKind kind = ServiceKind.Service, string? displayName = null) =>
        Add(new ServiceInfo(name, displayName ?? name, $"Windows' own description of {name}.", kind, start == ServiceStart.Automatic,
            new ServiceConfig(start, BinaryPath: $@"C:\Windows\system32\{name}.exe", Account: kind == ServiceKind.Driver ? "" : "LocalSystem")));

    public FakeServiceManager Add(ServiceInfo service)
    {
        _services[service.Name] = service;
        return this;
    }

    public ServiceConfig ConfigOf(string name) => _services[name].Config;

    /// <summary>Something other than the app changes the service.</summary>
    public void ChangeOutsideTheApp(string name, ServiceConfig config) => _services[name] = _services[name] with { Config = config };

    public IReadOnlyList<ServiceInfo> List() => _services.Values.ToList();

    public ServiceInfo? Find(string name) => _services.GetValueOrDefault(name);

    public void Configure(string name, ServiceConfig config)
    {
        if (!_services.TryGetValue(name, out var service))
            throw new InvalidOperationException($"The service {name} isn't installed.");
        if (Denied.Contains(name))
            throw new UnauthorizedAccessException("Access is denied.");
        Writes++;
        _services[name] = service with { Config = config };
    }
}

public sealed class FakeDevicePresence : IDevicePresence
{
    public HashSet<DeviceKind> Present { get; } = [];

    public bool Has(DeviceKind device) => Present.Contains(device);
}
