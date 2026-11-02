using System;
using System.Collections.Generic;
using BudsTray.Core;
using BudsTray.Protocol;

namespace BudsTray.UI;

public static class Labels
{
    public static string? Anc(AncMode? mode) => mode switch
    {
        AncMode.Off => Strings.Current.AncOff,
        AncMode.Transparency => Strings.Current.AncTransparency,
        AncMode.Low => Strings.Current.AncLow,
        AncMode.Mid => Strings.Current.AncMid,
        AncMode.High => Strings.Current.AncHigh,
        AncMode.Adaptive => Strings.Current.AncAdaptive,
        _ => null,
    };

    public static string Eq(EqPreset? preset) => preset switch
    {
        EqPreset.Dirac => "Dirac OPTEO",
        EqPreset.Rock => "Rock",
        EqPreset.Electronic => "Electronic",
        EqPreset.Pop => "Pop",
        EqPreset.Vocals => "Vocals",
        EqPreset.Classical => "Classical",
        EqPreset.Custom => "Custom",
        _ => "—",
    };

    public static string Remaining(TimeSpan remaining)
    {
        int minutes = (int)Math.Round(remaining.TotalMinutes / 10) * 10;
        if (minutes < 60)
            return string.Format(Strings.Current.RemainingMinutes, Math.Max(minutes, 10));
        return minutes % 60 == 0
            ? string.Format(Strings.Current.RemainingHours, minutes / 60)
            : string.Format(Strings.Current.RemainingHoursMinutes, minutes / 60, minutes % 60);
    }

    public static string LowBattery(LowBatteryAlert alert)
    {
        var parts = new List<string>();
        if (alert.Left is { } left)
            parts.Add(string.Format(Strings.Current.LeftLevel, left));
        if (alert.Right is { } right)
            parts.Add(string.Format(Strings.Current.RightLevel, right));
        return string.Format(Strings.Current.LowBattery, string.Join(", ", parts));
    }

    public static string LowBud(LowBatteryAlert alert) => (alert.Left, alert.Right) switch
    {
        (not null, not null) => Strings.Current.BothBudsLow,
        (not null, null) => Strings.Current.LeftBudLow,
        _ => Strings.Current.RightBudLow,
    };

    public static string Profile(AppProfile profile)
    {
        var parts = new List<string>();
        if (profile.Anc is { } anc)
            parts.Add(anc switch { AncMode.Transparency => "TRANSP", AncMode.Off => "OFF", _ => "ANC" });
        if (profile.LowLatency is { } latency)
            parts.Add(latency ? Strings.Current.ProfileLatencyOn : Strings.Current.ProfileLatencyOff);
        return string.Join(" · ", parts);
    }

    public static string Age(DateTime seenAt, DateTime now)
    {
        TimeSpan age = now - seenAt;
        if (age.TotalMinutes < 1)
            return Strings.Current.JustNow;
        if (age.TotalHours < 1)
            return string.Format(Strings.Current.MinutesAgo, (int)age.TotalMinutes);
        if (age.TotalDays < 1)
            return string.Format(Strings.Current.HoursAgo, (int)age.TotalHours);
        return string.Format(Strings.Current.DaysAgo, (int)age.TotalDays);
    }
}
