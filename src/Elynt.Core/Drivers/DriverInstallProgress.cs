namespace Elynt.Core.Drivers;

/// <summary>The steps of a driver install, in the order they happen.</summary>
public enum DriverInstallStage
{
    LookingUp,
    Downloading,
    Verifying,
    Installing,
}

/// <summary>One step of a driver install, streamed while it runs.</summary>
public sealed record DriverInstallProgress(DriverInstallStage Stage, string Message);
