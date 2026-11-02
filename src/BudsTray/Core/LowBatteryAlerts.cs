using BudsTray.Protocol;

namespace BudsTray.Core;

public sealed record LowBatteryAlert(int? Left, int? Right);

/// <summary>Decides when to warn: once at 20% and once at 10%, again only after the bud has really recharged.</summary>
public sealed class LowBatteryAlerts
{
    private static readonly int[] Thresholds = { 10, 20 };

    // The reading can flip between neighbouring 5% steps, which must not count as recharging.
    private const int Hysteresis = 5;

    private int? _leftWarnedAt, _rightWarnedAt;

    public LowBatteryAlert? Check(BatteryCell? left, BatteryCell? right)
    {
        int? l = Check(left, ref _leftWarnedAt);
        int? r = Check(right, ref _rightWarnedAt);
        return l is null && r is null ? null : new LowBatteryAlert(l, r);
    }

    private static int? Check(BatteryCell? cell, ref int? warnedAt)
    {
        if (cell is not { } c)
            return null;

        if (warnedAt is { } mark && c.Level > mark + Hysteresis)
            warnedAt = null;
        if (c.Charging)
            return null;

        foreach (int threshold in Thresholds)
        {
            if (c.Level > threshold)
                continue;
            if (warnedAt <= threshold)
                return null;
            warnedAt = threshold;
            return c.Level;
        }
        return null;
    }
}
