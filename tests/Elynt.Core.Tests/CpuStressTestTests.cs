using Elynt.Core.Tuning.Cpu;

namespace Elynt.Core.Tests;

/// <summary>The real in-app stress test, run briefly on the build machine's CPU.</summary>
public sealed class CpuStressTestTests
{
    [Fact]
    public void A_stable_CPU_passes()
    {
        var outcome = new CpuStressTest(threads: 2).Run(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        Assert.True(outcome.Passed, outcome.Detail);
    }

    [Fact]
    public void Stopping_it_throws_rather_than_passing()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.ThrowsAny<OperationCanceledException>(() => new CpuStressTest(threads: 2).Run(TimeSpan.FromMinutes(5), stop.Token));
    }
}
