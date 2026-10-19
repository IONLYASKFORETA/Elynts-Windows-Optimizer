using System.ServiceProcess;
using Elynt.Core.Setup;
using Microsoft.Win32;

namespace Elynt.Windows;

/// <summary>The real Service Control Manager, through ServiceController and sc.exe. Changes need admin rights.</summary>
public sealed class WindowsServiceControl : IServiceControl
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    public InstalledService? Query(string name)
    {
        using var controller = Find(name);
        if (controller is null)
            return null;

        // Reading the registration needs no admin rights, unlike asking the SCM for the service's config.
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        var imagePath = key?.GetValue("ImagePath", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";

        return new InstalledService(imagePath.Trim().Trim('"'), controller.Status == ServiceControllerStatus.Running);
    }

    public void Create(ServiceRegistration registration)
    {
        Sc("create", registration.Name, "binPath=", Quoted(registration.BinaryPath), "start=", "auto",
            "DisplayName=", registration.DisplayName);
        SetDetails(registration);
    }

    public void Configure(ServiceRegistration registration)
    {
        Sc("config", registration.Name, "binPath=", Quoted(registration.BinaryPath), "start=", "auto",
            "DisplayName=", registration.DisplayName);
        SetDetails(registration);
    }

    public void Start(string name)
    {
        using var controller = Find(name) ?? throw new InvalidOperationException($"The service {name} isn't installed.");
        if (controller.Status != ServiceControllerStatus.Running)
        {
            if (controller.Status != ServiceControllerStatus.StartPending)
                controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, StopTimeout);
        }
    }

    public void Stop(string name)
    {
        using var controller = Find(name);
        if (controller is null || controller.Status == ServiceControllerStatus.Stopped)
            return;
        if (controller.Status != ServiceControllerStatus.StopPending)
            controller.Stop();
        controller.WaitForStatus(ServiceControllerStatus.Stopped, StopTimeout);
    }

    public void Delete(string name) => Sc("delete", name);

    private static void SetDetails(ServiceRegistration registration)
    {
        Sc("description", registration.Name, registration.Description);
        // Restart after a crash: twice after 5 s, then after a minute; the failure count resets after a day.
        Sc("failure", registration.Name, "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/60000");
    }

    private static ServiceController? Find(string name)
    {
        var all = ServiceController.GetServices();
        ServiceController? match = null;
        foreach (var service in all)
        {
            if (match is null && string.Equals(service.ServiceName, name, StringComparison.OrdinalIgnoreCase))
                match = service;
            else
                service.Dispose();
        }
        return match;
    }

    /// <summary>sc.exe needs the quotes inside the binPath value when the path has spaces.</summary>
    private static string Quoted(string path) => $"\"{path}\"";

    private static void Sc(params string[] arguments) => SystemCommand.Run("sc.exe", arguments);
}
