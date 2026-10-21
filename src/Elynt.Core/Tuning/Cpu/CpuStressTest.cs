namespace Elynt.Core.Tuning.Cpu;

/// <summary>
/// Our own all-core stress test: every logical CPU runs the same fixed integer and floating-point workload over and
/// over and compares each result with the first one it got. An unstable undervolt shows up as a wrong result (or as a
/// WHEA error or crash, which the caller checks). Light and heavy phases alternate, because a too-negative Curve
/// Optimizer often fails on load changes rather than under steady full load.
/// </summary>
public sealed class CpuStressTest(int? threads = null) : ICpuStressTest
{
    private const int Rounds = 200_000;

    public StressOutcome Run(TimeSpan duration, CancellationToken cancellation)
    {
        var workers = threads ?? Environment.ProcessorCount;
        var deadline = DateTime.UtcNow + duration;
        var failure = (string?)null;
        var passes = 0L;

        var tasks = Enumerable.Range(0, workers).Select(worker => Task.Factory.StartNew(() =>
        {
            var expected = Workload(worker);
            var phase = 0;
            while (DateTime.UtcNow < deadline && Volatile.Read(ref failure) is null)
            {
                cancellation.ThrowIfCancellationRequested();
                if (Workload(worker) != expected)
                {
                    Interlocked.CompareExchange(ref failure, $"Thread {worker} returned a wrong result", null);
                    return;
                }
                Interlocked.Increment(ref passes);
                // Every eighth round rests briefly, so the cores swing between idle and full load.
                if (++phase % 8 == 0)
                    Thread.Sleep(2);
            }
        }, cancellation, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        try
        {
            Task.WaitAll(tasks, cancellation);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            throw new OperationCanceledException(cancellation);
        }

        return failure is null
            ? new StressOutcome(true, $"{passes:N0} rounds on {workers} threads, every result checked.")
            : new StressOutcome(false, failure);
    }

    /// <summary>A fixed mix of integer multiply, shifts and floating-point maths whose result never changes on a stable CPU.</summary>
    private static ulong Workload(int seed)
    {
        var x = 0x9E3779B97F4A7C15UL ^ (ulong)seed;
        var f = 1.0 + seed;
        for (var i = 0; i < Rounds; i++)
        {
            x ^= x << 13;
            x ^= x >> 7;
            x ^= x << 17;
            x *= 0xD1342543DE82EF95UL;
            f = Math.FusedMultiplyAdd(f, 1.000001, (x & 0xFF) * 1e-9);
            f = Math.Sqrt(f * f + 1.0) - 0.5;
        }
        return x ^ (ulong)BitConverter.DoubleToInt64Bits(f);
    }
}
