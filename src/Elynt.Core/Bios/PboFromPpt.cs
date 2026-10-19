namespace Elynt.Core.Bios;

/// <summary>
/// Tells whether PBO is on from the PPT (socket power) limit the Ryzen SMU is enforcing, read through PawnIO.
/// </summary>
public static class PboFromPpt
{
    /// <summary>
    /// AMD's stock PPT for each desktop TDP class: PPT = 1.35 × TDP (35, 45, 65, 105, 120 and 170 W parts). AMD's
    /// Ryzen Master guide says stock ("AMD Spec") limits can't be exceeded; PBO lifts them to the board's limits.
    /// </summary>
    private static readonly double[] StockPpt = [47.25, 60.75, 88, 142, 162, 230];

    private const double Tolerance = 1.5;

    /// <summary>
    /// On when the limit isn't any of AMD's stock limits (PBO, or PBO's manual limits, set by the BIOS); Off when it
    /// is one. Unknown for a reading too low to be a desktop limit, which is likely a mobile chip or a bad read.
    /// (Inference: Eco Mode sets a lower class's stock limit, so it correctly reads as PBO off.)
    /// </summary>
    public static ReadoutState State(double pptLimitWatts)
    {
        if (double.IsNaN(pptLimitWatts) || pptLimitWatts < 40)
            return ReadoutState.Unknown;
        return StockPpt.Any(stock => Math.Abs(stock - pptLimitWatts) <= Tolerance) ? ReadoutState.Off : ReadoutState.On;
    }
}
