using Elynt.Core.Setup;

namespace Elynt.Core.Tests.Setup;

public class OsSupportTests
{
    [Theory]
    [InlineData(22000)] // 21H2, the first Windows 11 build
    [InlineData(26100)] // 24H2
    [InlineData(26200)] // 25H2
    public void Windows_11_desktop_builds_are_supported(int build)
    {
        var result = OsSupport.Check(new OsDescription(build, IsServer: false));

        Assert.True(result.Supported);
    }

    [Fact]
    public void Windows_10_is_refused_with_an_explanation()
    {
        var result = OsSupport.Check(new OsDescription(19045, IsServer: false));

        Assert.False(result.Supported);
        Assert.Contains("Windows 11", result.Message);
    }

    [Fact]
    public void Windows_Server_is_refused_even_on_a_Windows_11_era_build()
    {
        var result = OsSupport.Check(new OsDescription(26100, IsServer: true));

        Assert.False(result.Supported);
        Assert.Contains("Windows Server", result.Message);
    }
}
