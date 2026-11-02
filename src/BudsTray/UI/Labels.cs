using System;
using System.Collections.Generic;
using BudsTray.Core;
using BudsTray.Protocol;

namespace BudsTray.UI;

public static class Labels
{
    public static string? Anc(AncMode? mode) => mode switch
    {
        AncMode.Off => "Шумоподавление выключено",
        AncMode.Transparency => "Прозрачность",
        AncMode.Low => "Шумоподавление: низкое",
        AncMode.Mid => "Шумоподавление: среднее",
        AncMode.High => "Шумоподавление: высокое",
        AncMode.Adaptive => "Шумоподавление: адаптивное",
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
            return $"≈ {Math.Max(minutes, 10)} мин";
        return minutes % 60 == 0 ? $"≈ {minutes / 60} ч" : $"≈ {minutes / 60} ч {minutes % 60} мин";
    }

    public static string LowBattery(LowBatteryAlert alert)
    {
        var parts = new List<string>();
        if (alert.Left is { } left)
            parts.Add($"левый {left} %");
        if (alert.Right is { } right)
            parts.Add($"правый {right} %");
        return "Низкий заряд: " + string.Join(", ", parts);
    }

    public static string Profile(AppProfile profile)
    {
        var parts = new List<string>();
        if (profile.Anc is { } anc)
            parts.Add(anc switch { AncMode.Transparency => "TRANSP", AncMode.Off => "OFF", _ => "ANC" });
        if (profile.LowLatency is { } latency)
            parts.Add(latency ? "ЗАДЕРЖКА ВКЛ" : "ЗАДЕРЖКА ВЫКЛ");
        return string.Join(" · ", parts);
    }

    public static string Age(DateTime seenAt, DateTime now)
    {
        TimeSpan age = now - seenAt;
        if (age.TotalMinutes < 1)
            return "только что";
        if (age.TotalHours < 1)
            return $"{(int)age.TotalMinutes} мин назад";
        if (age.TotalDays < 1)
            return $"{(int)age.TotalHours} ч назад";
        return $"{(int)age.TotalDays} дн назад";
    }
}
