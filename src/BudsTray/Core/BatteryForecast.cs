using System;
using System.Collections.Generic;
using BudsTray.Protocol;

namespace BudsTray.Core;

public sealed record BatteryEstimate(TimeSpan? Remaining, double? LeftPerHour, double? RightPerHour);

/// <summary>
/// Works out how fast the buds drain from the recent history. The buds report charge in 5% steps,
/// so a rate is only trusted once it spans a few of them.
/// </summary>
public static class BatteryForecast
{
    // A longer silence means the buds were away, and that time must not count as use.
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MinUse = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan MaxRemaining = TimeSpan.FromHours(24);
    private static readonly TimeSpan Lookback = TimeSpan.FromDays(14);
    private const int MinDrop = 10;
    private const int EnoughDrop = 30;

    public static BatteryEstimate Estimate(IReadOnlyList<BatterySample> samples, DateTime nowUtc, BatteryCell? left, BatteryCell? right)
    {
        double? leftRate = Rate(samples, nowUtc, s => s.Left);
        double? rightRate = Rate(samples, nowUtc, s => s.Right);
        return new BatteryEstimate(Remaining(left, leftRate, right, rightRate), leftRate, rightRate);
    }

    private static TimeSpan? Remaining(BatteryCell? left, double? leftRate, BatteryCell? right, double? rightRate)
    {
        if (left is { Charging: true } || right is { Charging: true })
            return null;

        TimeSpan? shortest = Shorter(ForBud(left, leftRate), ForBud(right, rightRate));
        return shortest > MaxRemaining ? null : shortest;
    }

    private static TimeSpan? ForBud(BatteryCell? cell, double? rate) =>
        cell is { } c && rate is { } r ? TimeSpan.FromHours(c.Level / r) : null;

    private static TimeSpan? Shorter(TimeSpan? a, TimeSpan? b) =>
        a is { } x && b is { } y ? (x < y ? x : y) : a ?? b;

    /// <summary>Percent per hour, newest use first, or null while there is too little to go on.</summary>
    private static double? Rate(IReadOnlyList<BatterySample> samples, DateTime nowUtc, Func<BatterySample, BatteryCell?> bud)
    {
        DateTime oldest = nowUtc - Lookback;
        TimeSpan use = TimeSpan.Zero;
        int drop = 0;

        for (int i = samples.Count - 1; i > 0 && drop < EnoughDrop; i--)
        {
            BatterySample earlier = samples[i - 1], later = samples[i];
            if (earlier.At < oldest)
                break;

            TimeSpan gap = later.At - earlier.At;
            if (gap > MaxGap || bud(earlier) is not { Charging: false } from || bud(later) is not { Charging: false } to || to.Level > from.Level)
                continue;

            use += gap;
            drop += from.Level - to.Level;
        }

        return drop >= MinDrop && use >= MinUse ? drop / use.TotalHours : null;
    }
}
