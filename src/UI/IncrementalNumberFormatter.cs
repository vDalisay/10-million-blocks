using System;

namespace TenMillionBlocks.UI;

public static class IncrementalNumberFormatter
{
    private static readonly string[] Suffixes = ["", "K", "M", "B", "T", "Qa", "Qi"];

    public static string Format(long value)
    {
        long absolute = value == long.MinValue ? long.MaxValue : Math.Abs(value);
        if (absolute < 10_000) return value.ToString("N0");

        double scaled = value;
        int suffix = 0;
        while (Math.Abs(scaled) >= 1000.0 && suffix < Suffixes.Length - 1)
        {
            scaled /= 1000.0;
            suffix++;
        }

        string format = Math.Abs(scaled) >= 100.0 ? "0" : Math.Abs(scaled) >= 10.0 ? "0.0" : "0.00";
        return scaled.ToString(format) + Suffixes[suffix];
    }
}
