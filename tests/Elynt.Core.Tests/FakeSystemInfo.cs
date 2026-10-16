namespace Elynt.Core.Tests;

public sealed class FakeSystemInfo : ISystemInfo
{
    public string OsBuild { get; set; } = "26100.1";
}
