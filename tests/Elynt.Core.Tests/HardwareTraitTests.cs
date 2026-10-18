using Elynt.Core.Tweaks;

namespace Elynt.Core.Tests;

/// <summary>Hardware the gaming warnings depend on, recognised from the names Windows reports.</summary>
public class HardwareTraitTests
{
    [Theory]
    [InlineData("AMD Ryzen 9 7950X3D 16-Core Processor")]
    [InlineData("AMD Ryzen 9 7900X3D 12-Core Processor")]
    [InlineData("AMD Ryzen 9 9950X3D 16-Core Processor")]
    [InlineData("AMD Ryzen 9 9900X3D 12-Core Processor")]
    public void Dual_CCD_X3D_CPUs_are_recognised(string cpu) =>
        Assert.Equal(HardwareTraits.DualCcdX3dCpu, HardwareNames.FromCpuName(cpu));

    [Theory]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor")]
    [InlineData("AMD Ryzen 7 9800X3D 8-Core Processor")]
    [InlineData("AMD Ryzen 7 5800X3D 8-Core Processor")]
    [InlineData("AMD Ryzen 9 7950X 16-Core Processor")]
    [InlineData("13th Gen Intel(R) Core(TM) i7-13700K")]
    [InlineData("")]
    public void Single_CCD_and_other_CPUs_are_not(string cpu) =>
        Assert.Equal(HardwareTraits.None, HardwareNames.FromCpuName(cpu));

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4090")]
    [InlineData("NVIDIA GeForce RTX 4070 Ti SUPER")]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU")]
    [InlineData("NVIDIA GeForce RTX 5070")]
    public void RTX_40_and_50_cards_can_use_Frame_Generation(string gpu) =>
        Assert.Equal(HardwareTraits.FrameGenerationGpu, HardwareNames.FromGpuName(gpu));

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3080")]
    [InlineData("NVIDIA GeForce GTX 1080 Ti")]
    [InlineData("AMD Radeon RX 7900 XTX")]
    [InlineData("Intel(R) Arc(TM) A770 Graphics")]
    public void Older_and_other_cards_cannot(string gpu) =>
        Assert.Equal(HardwareTraits.None, HardwareNames.FromGpuName(gpu));
}
