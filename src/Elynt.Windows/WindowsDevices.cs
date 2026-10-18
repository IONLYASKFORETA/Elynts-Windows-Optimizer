using System.Runtime.InteropServices;
using Elynt.Core.Devices;

namespace Elynt.Windows;

/// <summary>
/// Plug and Play devices. Present devices and their state are read with the Configuration Manager API, so nothing
/// depends on the display language; enabling and disabling go through pnputil, which keeps the change across
/// restarts. Needs admin rights to change anything.
/// </summary>
public sealed partial class WindowsDevices : IDevices
{
    private const int CrSuccess = 0;
    private const uint CmGetIdListFilterEnumerator = 0x1;
    private const uint CmGetIdListFilterPresent = 0x100;
    private const uint CmLocateDevnodeNormal = 0;
    private const uint DnHasProblem = 0x400;
    private const uint CmProbDisabled = 22;

    /// <summary>pnputil's exit code when the change waits for a restart.</summary>
    private const int ErrorSuccessRebootRequired = 3010;

    public IReadOnlyList<string> FindByHardwareId(string hardwareId)
    {
        // Instance IDs start with the enumerator and device ID (ACPI\PNP0103\0), so filter the enumerator's list.
        var enumerator = hardwareId.Split('\\')[0];
        var flags = CmGetIdListFilterEnumerator | CmGetIdListFilterPresent;
        if (CM_Get_Device_ID_List_Size(out var size, enumerator, flags) != CrSuccess || size == 0)
            return [];
        var buffer = new char[size];
        if (CM_Get_Device_ID_List(enumerator, buffer, size, flags) != CrSuccess)
            return [];
        return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(id => id.StartsWith(hardwareId + "\\", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public bool? IsEnabled(string instanceId)
    {
        if (CM_Locate_DevNode(out var node, instanceId, CmLocateDevnodeNormal) != CrSuccess)
            return null;
        if (CM_Get_DevNode_Status(out var status, out var problem, node, 0) != CrSuccess)
            return null;
        return !((status & DnHasProblem) != 0 && problem == CmProbDisabled);
    }

    public void SetEnabled(string instanceId, bool enabled) =>
        CommandLine.Run("pnputil.exe", [ErrorSuccessRebootRequired], enabled ? "/enable-device" : "/disable-device", instanceId);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_List_SizeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CM_Get_Device_ID_List_Size(out uint length, string filter, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_ListW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CM_Get_Device_ID_List(string filter, [Out] char[] buffer, uint length, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_Status")]
    private static partial int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);
}
