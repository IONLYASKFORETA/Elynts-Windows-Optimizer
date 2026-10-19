using Elynt.Core.Sensors;

namespace Elynt.Core.Tests;

public sealed class FakeSensorSource(string name, params SensorReading[] readings) : ISensorSource
{
    public string Name => name;

    public Exception? Fail { get; set; }

    public IReadOnlyList<SensorReading> Read() => Fail is null ? readings : throw Fail;
}
