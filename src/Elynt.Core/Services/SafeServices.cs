namespace Elynt.Core.Services;

/// <summary>One service reviewed as safe to change in the normal view.</summary>
/// <param name="Explanation">What it does and what turning it to Manual costs, in plain words.</param>
/// <param name="NotNeededWithout">Manual is recommended when the PC has no such device.</param>
public sealed record SafeService(string Name, string Explanation, DeviceKind? NotNeededWithout = null);

/// <summary>
/// The reviewed safe-to-change list. Everything here keeps Windows working when set to Manual: Manual services still
/// start when something asks for them. Disabled is Advanced only, for these too.
/// </summary>
public static class SafeServices
{
    public static IReadOnlyList<SafeService> All { get; } =
    [
        new("Spooler",
            "Print Spooler queues documents for printers, including Microsoft Print to PDF. With no printer it only " +
            "costs a little memory; on Manual it still starts when you print.",
            DeviceKind.Printer),
        new("PrintNotify",
            "Printer Extensions and Notifications shows printer pop-ups and vendor printer apps. Only useful with a printer.",
            DeviceKind.Printer),
        new("Fax", "Sends and receives faxes through a fax modem. Almost no PC needs it."),
        new("bthserv",
            "Bluetooth Support Service finds and pairs Bluetooth devices. Not needed on a PC with no Bluetooth adapter.",
            DeviceKind.BluetoothAdapter),
        new("BTAGService",
            "Bluetooth Audio Gateway lets Bluetooth headsets act as a hands-free headset (calls, microphone).",
            DeviceKind.BluetoothAdapter),
        new("BthAvctpSvc",
            "AVCTP service carries play, pause and volume buttons from Bluetooth headphones and speakers.",
            DeviceKind.BluetoothAdapter),
        new("WSearch",
            "Windows Search indexes files so Start and Explorer search finds them fast. Turned down, search still works but is slower."),
        new("SysMain",
            "SysMain (Superfetch) preloads apps you use often into memory. Helps on hard drives, does little on an SSD."),
        new("MapsBroker", "Downloaded Maps Manager updates offline maps for the Maps app."),
        new("lfsvc", "Geolocation Service tells apps where the PC is. Turned down, apps can't use location."),
        new("RetailDemo", "Retail Demo runs the shop-display mode for PCs on sale in stores."),
        new("WMPNetworkSvc", "Windows Media Player Network Sharing shares your media library with other devices on the network."),
        new("WerSvc", "Windows Error Reporting sends crash reports to Microsoft. Crashes still happen the same; they just aren't reported."),
        new("PhoneSvc", "Phone Service manages telephony state for calling apps."),
        new("TrkWks", "Distributed Link Tracking Client keeps shortcuts working when files move between NTFS drives on a network."),
    ];

    private static readonly Dictionary<string, SafeService> ByName = All.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

    public static SafeService? Find(string name) => ByName.GetValueOrDefault(name);
}
