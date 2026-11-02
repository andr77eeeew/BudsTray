using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BudsTray.Protocol;

namespace BudsTray.Core;

public readonly record struct BatterySample(DateTime At, BatteryCell? Left, BatteryCell? Right);

/// <summary>
/// An append-only record of the charge, kept on disk so that nothing stays in memory between panel
/// openings. A sample is written on a change, and at least every 10 minutes so that a quiet stretch
/// still counts as time in use.
/// </summary>
public sealed class BatteryHistory
{
    private const string TimeFormat = "yyyy-MM-ddTHH:mm:ssZ";
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(10);

    private readonly string _path;
    private BatterySample? _last;

    public BatteryHistory(string path) => _path = path;

    public static string DefaultPath => Path.Combine(Log.DataDirectory, "battery.csv");

    public void Record(DateTime nowUtc, BatteryCell? left, BatteryCell? right)
    {
        if (left is null && right is null)
            return;
        if (_last is { } last && last.Left == left && last.Right == right && nowUtc - last.At < Heartbeat)
            return;

        var sample = new BatterySample(nowUtc, left, right);
        _last = sample;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, Format(sample) + Environment.NewLine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Could not save battery history: {e.Message}");
        }
    }

    /// <summary>Samples in order of time. Damaged lines are skipped, a broken file must not cost the rest.</summary>
    public List<BatterySample> Load(DateTime nowUtc)
    {
        try
        {
            DateTime oldest = nowUtc - Retention;
            return File.ReadLines(_path)
                .Select(Parse)
                .Where(s => s is { } sample && sample.At >= oldest)
                .Select(s => s!.Value)
                .OrderBy(s => s.At)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new List<BatterySample>();
        }
    }

    public void Compact(DateTime nowUtc)
    {
        try
        {
            if (!File.Exists(_path))
                return;
            List<BatterySample> kept = Load(nowUtc);
            if (kept.Count < File.ReadLines(_path).Count())
                File.WriteAllLines(_path, kept.Select(Format));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Could not compact battery history: {e.Message}");
        }
    }

    /// <summary>Splits a period into columns, each holding the last reading that fell into it.</summary>
    public static (int? Left, int? Right)[] Columns(IEnumerable<BatterySample> samples, DateTime nowUtc, TimeSpan period, int count)
    {
        var columns = new (int? Left, int? Right)[count];
        DateTime start = nowUtc - period;
        foreach (BatterySample sample in samples)
        {
            if (sample.At < start || sample.At > nowUtc)
                continue;
            int index = Math.Min(count - 1, (int)((sample.At - start) / period * count));
            columns[index] = (sample.Left?.Level ?? columns[index].Left, sample.Right?.Level ?? columns[index].Right);
        }
        return columns;
    }

    private static string Format(BatterySample s) =>
        $"{s.At.ToString(TimeFormat, CultureInfo.InvariantCulture)},{Cell(s.Left)},{Cell(s.Right)}";

    private static string Cell(BatteryCell? cell) => cell is { } c ? $"{c.Level},{(c.Charging ? 1 : 0)}" : ",";

    private static BatterySample? Parse(string line)
    {
        string[] parts = line.Split(',');
        if (parts.Length != 5)
            return null;
        if (!DateTime.TryParseExact(parts[0], TimeFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime at))
            return null;
        return new BatterySample(at, ParseCell(parts[1], parts[2]), ParseCell(parts[3], parts[4]));
    }

    private static BatteryCell? ParseCell(string level, string charging) =>
        int.TryParse(level, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            ? new BatteryCell(value, charging == "1")
            : null;
}
