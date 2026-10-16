using System.Text.RegularExpressions;

namespace Elynt.Core.Tweaks;

/// <summary>Hardware traits recognised from the CPU and GPU names Windows reports.</summary>
public static partial class HardwareNames
{
    /// <summary><see cref="HardwareTraits.DualCcdX3dCpu"/> for the Ryzen X3D parts with two CCDs.</summary>
    public static HardwareTraits FromCpuName(string cpuName) =>
        DualCcdX3d().IsMatch(cpuName) ? HardwareTraits.DualCcdX3dCpu : HardwareTraits.None;

    /// <summary><see cref="HardwareTraits.FrameGenerationGpu"/> for NVIDIA GeForce RTX 40 and 50 series cards.</summary>
    public static HardwareTraits FromGpuName(string gpuName) =>
        FrameGeneration().IsMatch(gpuName) ? HardwareTraits.FrameGenerationGpu : HardwareTraits.None;

    // 7900X3D, 7950X3D, 9900X3D, 9950X3D. The 5800X3D, 7800X3D and 9800X3D have one CCD.
    [GeneratedRegex(@"\b[79]9[05]0X3D\b", RegexOptions.IgnoreCase)]
    private static partial Regex DualCcdX3d();

    [GeneratedRegex(@"\bRTX\s*[45]0\d0\b", RegexOptions.IgnoreCase)]
    private static partial Regex FrameGeneration();
}
