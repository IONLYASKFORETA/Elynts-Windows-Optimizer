using System.Runtime.InteropServices;
using Elynt.Core;
using Elynt.Core.Setup;
using Elynt.Core.Tweaks;
using Microsoft.Win32;

namespace Elynt.Windows;

public sealed partial class WindowsSystemInfo : ISystemInfo
{
    private const string DisplayAdapterClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public string OsBuild { get; } = ReadBuild();

    public WindowsEdition Edition { get; } = ReadEdition();

    public HardwareTraits Hardware { get; } = ReadHardware();

    public DateTimeOffset BootTime { get; } =
        DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>What <see cref="OsSupport"/> needs to decide whether the app may run here. Reads only.</summary>
    public static OsDescription Describe()
    {
        using var machine = Machine();
        using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out var number) ? number : 0;
        // "Client" on desktop Windows; "Server" or "Server Core" on Windows Server.
        var installationType = key?.GetValue("InstallationType") as string ?? "";
        return new OsDescription(build, installationType.StartsWith("Server", StringComparison.OrdinalIgnoreCase));
    }

    private static RegistryKey Machine() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    private static string ReadBuild()
    {
        using var machine = Machine();
        using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        return $"{key?.GetValue("CurrentBuild")}.{key?.GetValue("UBR")}";
    }

    private static WindowsEdition ReadEdition()
    {
        using var machine = Machine();
        using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        return (key?.GetValue("EditionID") as string) switch
        {
            "Core" or "CoreN" or "CoreSingleLanguage" or "CoreCountrySpecific" => WindowsEdition.Home,
            "Professional" or "ProfessionalN" or "ProfessionalWorkstation" or "ProfessionalWorkstationN" => WindowsEdition.Pro,
            "Education" or "EducationN" or "ProfessionalEducation" or "ProfessionalEducationN" => WindowsEdition.Education,
            "Enterprise" or "EnterpriseN" or "EnterpriseS" or "EnterpriseSN" => WindowsEdition.Enterprise,
            _ => WindowsEdition.Unknown,
        };
    }

    private static HardwareTraits ReadHardware()
    {
        var traits = HardwareTraits.None;
        using var machine = Machine();

        using (var cpu = machine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
        {
            traits |= (cpu?.GetValue("VendorIdentifier") as string) switch
            {
                "AuthenticAMD" => HardwareTraits.AmdCpu,
                "GenuineIntel" => HardwareTraits.IntelCpu,
                _ => HardwareTraits.None,
            };
            traits |= HardwareNames.FromCpuName((cpu?.GetValue("ProcessorNameString") as string) ?? "");
        }

        using (var adapters = machine.OpenSubKey(DisplayAdapterClass))
        {
            foreach (var name in adapters?.GetSubKeyNames() ?? [])
            {
                RegistryKey? adapter;
                try
                {
                    adapter = adapters!.OpenSubKey(name);
                }
                catch (System.Security.SecurityException)
                {
                    continue; // The "Properties" subkey is not readable; it is not an adapter anyway.
                }
                using (adapter)
                {
                    var device = (adapter?.GetValue("MatchingDeviceId") as string)?.ToUpperInvariant() ?? "";
                    traits |= HardwareNames.FromGpuName((adapter?.GetValue("DriverDesc") as string) ?? "");
                    if (device.Contains("VEN_10DE")) traits |= HardwareTraits.NvidiaGpu;
                    else if (device.Contains("VEN_1002")) traits |= HardwareTraits.AmdGpu;
                    else if (device.Contains("VEN_8086")) traits |= HardwareTraits.IntelGpu;
                }
            }
        }

        // A system battery means a laptop (or a tablet); 128 is "no system battery".
        if (GetSystemPowerStatus(out var power) && power.BatteryFlag != 128 && power.BatteryFlag != 255)
            traits |= HardwareTraits.Laptop;

        return traits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus status);
}
