using Elynt.Core.Bios;

namespace Elynt.Core.Tests;

public sealed class FakeHardwareInfo : IHardwareInfo
{
    public HardwareSnapshot Snapshot { get; set; } = new(
        "ASUSTeK COMPUTER INC.", "ROG STRIX Z790-E GAMING WIFI", "13th Gen Intel(R) Core(TM) i7-13700K",
        [new MemoryModule(MemoryType.Ddr5, RatedMts: 4800, ConfiguredMts: 4800)],
        ReadoutState.NotApplicable);

    public Exception? Fail { get; set; }

    public HardwareSnapshot Read() => Fail is null ? Snapshot : throw Fail;
}
