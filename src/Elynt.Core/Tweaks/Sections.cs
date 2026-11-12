namespace Elynt.Core.Tweaks;

/// <summary>The suite's sections, in sidebar order.</summary>
public static class Sections
{
    public const string Gaming = "Gaming";
    public const string DebloatAndPrivacy = "Debloat & Privacy";
    public const string Security = "Security";
    public const string Apps = "Apps";
    public const string WindowsUpdate = "Windows Update";
    public const string StartupAndServices = "Startup & Services";
    public const string Cleanup = "Cleanup";
    public const string Repair = "Repair";
    public const string Drivers = "Drivers";
    public const string Overclocking = "Overclocking";

    /// <summary>In the sidebar's groups: PLAY's sections, then WINDOWS', then TOOLS'. Home's section cards follow it.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Gaming, Overclocking, Drivers,
        DebloatAndPrivacy, Security, WindowsUpdate, StartupAndServices,
        Apps, Cleanup, Repair,
    ];
}
