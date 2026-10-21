using Elynt.Core.Setup;

namespace Elynt.Core.Tests.Setup;

/// <summary>An in-memory Service Control Manager.</summary>
public sealed class FakeServiceControl : IServiceControl
{
    public sealed class Service
    {
        public required string BinaryPath { get; set; }
        public bool Running { get; set; }
        public bool StartsAutomatically { get; set; }
        public bool RestartsOnFailure { get; set; }
    }

    private readonly Dictionary<string, Service> _services = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Set to make the next Start fail, as when the binary is missing or crashes at once.</summary>
    public Func<Service, bool>? StartFails { get; set; }

    public Service? Get(string name) => _services.GetValueOrDefault(name);

    public void Add(string name, string binaryPath, bool running) =>
        _services[name] = new Service { BinaryPath = binaryPath, Running = running, StartsAutomatically = true };

    public InstalledService? Query(string name) =>
        _services.TryGetValue(name, out var s) ? new InstalledService(s.BinaryPath, s.Running) : null;

    public void Create(ServiceRegistration registration)
    {
        if (_services.ContainsKey(registration.Name))
            throw new InvalidOperationException("The service already exists.");
        _services[registration.Name] = new Service
        {
            BinaryPath = registration.BinaryPath,
            StartsAutomatically = true,
            RestartsOnFailure = true,
        };
    }

    public void Configure(ServiceRegistration registration)
    {
        var s = Existing(registration.Name);
        s.BinaryPath = registration.BinaryPath;
        s.StartsAutomatically = true;
        s.RestartsOnFailure = true;
    }

    public void Start(string name)
    {
        var s = Existing(name);
        if (StartFails?.Invoke(s) == true)
            throw new InvalidOperationException("The service did not start.");
        s.Running = true;
    }

    public void Stop(string name) => Existing(name).Running = false;

    public void Delete(string name)
    {
        if (Existing(name).Running)
            throw new InvalidOperationException("A running service is only marked for deletion.");
        _services.Remove(name);
    }

    private Service Existing(string name) =>
        _services.GetValueOrDefault(name) ?? throw new InvalidOperationException($"No service named {name}.");
}

/// <summary>Folders holding a service build, each identified by its version. Files of a running service are locked.</summary>
public sealed class FakeServiceFiles(FakeServiceControl? services = null) : IServiceFiles
{
    private readonly Dictionary<string, string> _folders = new(StringComparer.OrdinalIgnoreCase);

    public bool CopyFails { get; set; }

    public void Put(string folder, string version) => _folders[folder] = version;

    public bool Exists(string folder) => _folders.ContainsKey(folder);

    public string? VersionAt(string folder) => _folders.GetValueOrDefault(folder);

    public void Mirror(string from, string to)
    {
        if (services?.Get(ServiceSetup.ServiceName) is { Running: true } running &&
            running.BinaryPath.StartsWith(to, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The service's files are in use.");
        if (CopyFails)
            throw new IOException("Disk full.");
        _folders[to] = _folders[from];
    }

    public void DeleteFolder(string folder) => _folders.Remove(folder);
}
