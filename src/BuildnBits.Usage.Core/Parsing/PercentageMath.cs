namespace BuildnBits.Usage.Core.Parsing;

public static class PercentageMath
{
    public static double ClampPercent(double value) =>
        Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 100);

    public static double RemainingFromUsed(double usedPercent) =>
        ClampPercent(100 - usedPercent);

    public static int DisplayPercent(double remaining) =>
        (int)ClampPercent(remaining);

    public static double LowestRemaining(IEnumerable<double> remaining) =>
        remaining.DefaultIfEmpty(0).Min();
}
