using System;

namespace TenMillionBlocks.Progression;

/// <summary>Pure scoring contract for the speed-based end-of-world black-hole bonus.</summary>
public static class CompletionScore
{
    public const int MinimumPercent = 20;
    public const int MaximumPercent = 100;
    public const double StepSeconds = 5.0 * 60.0;

    public static int CalculatePercent(double clearSeconds)
    {
        double safeSeconds = Math.Max(0.0, clearSeconds);
        int fiveMinuteSteps = (int)Math.Floor(safeSeconds / StepSeconds);
        return Math.Max(MinimumPercent, MaximumPercent - fiveMinuteSteps * 10);
    }

    public static long CalculateBonus(long initialBlockCount, int scorePercent)
    {
        long blocks = Math.Max(0L, initialBlockCount);
        int percent = Math.Clamp(scorePercent, MinimumPercent, MaximumPercent);
        double exact = blocks * (percent / 100.0);
        return checked((long)Math.Round(exact, MidpointRounding.AwayFromZero));
    }

    public static long CalculateBonus(long initialBlockCount, double clearSeconds)
        => CalculateBonus(initialBlockCount, CalculatePercent(clearSeconds));
}
