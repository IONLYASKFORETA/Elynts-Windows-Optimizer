namespace Elynt.Core.Startup;

/// <summary>
/// Task Manager's startup impact thresholds (Microsoft: "How to manage startup apps", impact column): High is more
/// than 1 s of CPU or 3 MB of disk; Medium is 300 ms–1 s of CPU or 300 KB–3 MB of disk; Low is less than both.
/// </summary>
public static class StartupImpactRules
{
    public static StartupImpact Classify(string? executablePath, IReadOnlyList<StartupMeasurement> measurements)
    {
        if (executablePath is null)
            return StartupImpact.NotMeasured;

        var mine = measurements
            .Where(m => m.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (mine.Count == 0)
            return StartupImpact.NotMeasured;

        var cpu = TimeSpan.FromTicks(mine.Sum(m => m.Cpu.Ticks));
        var disk = mine.Sum(m => m.DiskBytes);
        if (cpu > TimeSpan.FromSeconds(1) || disk > 3 * 1024 * 1024)
            return StartupImpact.High;
        if (cpu >= TimeSpan.FromMilliseconds(300) || disk >= 300 * 1024)
            return StartupImpact.Medium;
        return StartupImpact.Low;
    }
}
