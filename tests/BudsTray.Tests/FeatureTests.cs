using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BudsTray.Core;
using BudsTray.Protocol;
using Xunit;

namespace BudsTray.Tests;

public class BatteryHistoryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BudsTrayTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "battery.csv");

    private static BatteryCell Cell(int level, bool charging = false) => new(level, charging);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Record_RoundTripsValuesAndMissingBuds()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Now, Cell(85), null);
        history.Record(Now.AddMinutes(1), Cell(80, charging: true), Cell(75));

        List<BatterySample> samples = history.Load(Now.AddMinutes(1));

        Assert.Equal(2, samples.Count);
        Assert.Equal(new BatterySample(Now, Cell(85), null), samples[0]);
        Assert.Equal(new BatterySample(Now.AddMinutes(1), Cell(80, true), Cell(75)), samples[1]);
    }

    [Fact]
    public void Record_SkipsRepeatsUntilTheHeartbeat()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Now, Cell(85), Cell(80));
        history.Record(Now.AddMinutes(2), Cell(85), Cell(80));
        history.Record(Now.AddMinutes(10), Cell(85), Cell(80));

        Assert.Equal(2, history.Load(Now.AddMinutes(10)).Count);
    }

    [Fact]
    public void Record_IgnoresAReportWithNoBuds()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Now, null, null);

        Assert.Empty(history.Load(Now));
    }

    [Fact]
    public void Load_SkipsDamagedLines()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllLines(FilePath, new[] { "garbage", "2026-11-02T11:00:00Z,85,0,80,0", "2026-11-02T11:01:00Z,85" });

        Assert.Single(new BatteryHistory(FilePath).Load(Now));
    }

    [Fact]
    public void Compact_DropsOnlyWhatIsTooOld()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Now.AddDays(-40), Cell(90), Cell(90));
        history.Record(Now.AddDays(-1), Cell(70), Cell(70));

        history.Compact(Now);

        Assert.Single(File.ReadAllLines(FilePath));
        Assert.Equal(70, history.Load(Now).Single().Left!.Value.Level);
    }

    [Fact]
    public void Columns_PlacesSamplesAndKeepsTheLastInEach()
    {
        var samples = new[]
        {
            new BatterySample(Now.AddHours(-30), Cell(99), Cell(99)),
            new BatterySample(Now.AddHours(-23.9), Cell(90), Cell(85)),
            new BatterySample(Now.AddHours(-23.8), Cell(88), null),
            new BatterySample(Now, Cell(40), Cell(35)),
        };

        (int? Left, int? Right)[] columns = BatteryHistory.Columns(samples, Now, TimeSpan.FromDays(1), 24);

        Assert.Equal(24, columns.Length);
        Assert.Equal(((int?)88, (int?)85), columns[0]);
        Assert.Equal(((int?)40, (int?)35), columns[23]);
        Assert.Equal(((int?)null, (int?)null), columns[12]);
    }
}

public class BatteryForecastTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private static BatteryCell Cell(int level, bool charging = false) => new(level, charging);

    /// <summary>Levels of both buds, one sample every <paramref name="stepMinutes"/>, the last one at Now.</summary>
    private static List<BatterySample> Series(int stepMinutes, params int[] levels) =>
        levels.Select((level, i) => new BatterySample(Now.AddMinutes((i - levels.Length + 1) * stepMinutes), Cell(level), Cell(level))).ToList();

    [Fact]
    public void Estimate_FromSteadyDrain()
    {
        // 15% in 90 minutes is 10% per hour, so 70% is 7 hours.
        BatteryEstimate estimate = BatteryForecast.Estimate(Series(10, 85, 85, 85, 80, 80, 80, 75, 75, 75, 70), Now, Cell(70), Cell(70));

        Assert.Equal(10, estimate.LeftPerHour!.Value, precision: 3);
        Assert.Equal(TimeSpan.FromHours(7), estimate.Remaining);
    }

    [Fact]
    public void Estimate_UsesTheEmptierBud()
    {
        BatteryEstimate estimate = BatteryForecast.Estimate(Series(10, 85, 85, 85, 80, 80, 80, 75, 75, 75, 70), Now, Cell(70), Cell(35));

        Assert.Equal(TimeSpan.FromHours(3.5), estimate.Remaining);
    }

    [Fact]
    public void Estimate_NeedsEnoughDrop()
    {
        BatteryEstimate estimate = BatteryForecast.Estimate(Series(10, 85, 85, 85, 80, 80, 80), Now, Cell(80), Cell(80));

        Assert.Null(estimate.LeftPerHour);
        Assert.Null(estimate.Remaining);
    }

    [Fact]
    public void Estimate_NeedsEnoughTime()
    {
        BatteryEstimate estimate = BatteryForecast.Estimate(Series(5, 85, 75), Now, Cell(75), Cell(75));

        Assert.Null(estimate.LeftPerHour);
    }

    [Fact]
    public void Estimate_DoesNotCountTimeAway()
    {
        var samples = new List<BatterySample>
        {
            new(Now.AddHours(-12), Cell(90), Cell(90)),
            new(Now.AddHours(-11.5), Cell(80), Cell(80)),
            new(Now.AddHours(-1), Cell(80), Cell(80)),
            new(Now.AddMinutes(-55), Cell(80), Cell(80)),
        };

        BatteryEstimate estimate = BatteryForecast.Estimate(samples, Now, Cell(80), Cell(80));

        // Only the half hour of use counts, and the 10.5 hour gap does not; the first pair exceeds the gap limit too.
        Assert.Null(estimate.LeftPerHour);
    }

    [Fact]
    public void Estimate_IgnoresChargingAndRecharge()
    {
        var samples = new List<BatterySample>
        {
            new(Now.AddMinutes(-40), Cell(50), Cell(50)),
            new(Now.AddMinutes(-30), Cell(70, charging: true), Cell(70, charging: true)),
            new(Now.AddMinutes(-20), Cell(90), Cell(90)),
            new(Now.AddMinutes(-10), Cell(85), Cell(85)),
            new(Now, Cell(80), Cell(80)),
        };

        BatteryEstimate estimate = BatteryForecast.Estimate(samples, Now, Cell(80), Cell(80));

        // 10% in 20 minutes; the climb from 50 to 90 adds nothing.
        Assert.Equal(30, estimate.LeftPerHour!.Value, precision: 3);
    }

    [Fact]
    public void Estimate_NoRemainingWhileCharging()
    {
        BatteryEstimate estimate = BatteryForecast.Estimate(Series(10, 85, 85, 85, 80, 80, 80, 75, 75, 75, 70), Now, Cell(70, charging: true), Cell(70));

        Assert.Null(estimate.Remaining);
    }

    [Fact]
    public void Estimate_DismissesAnImplausiblyLongResult()
    {
        BatteryEstimate estimate = BatteryForecast.Estimate(
            Enumerable.Range(0, 400).Select(i => new BatterySample(Now.AddMinutes((i - 399) * 10.0), Cell(i < 200 ? 100 : 90), Cell(i < 200 ? 100 : 90))).ToList(),
            Now, Cell(90), Cell(90));

        Assert.Null(estimate.Remaining);
    }
}

public class LowBatteryAlertsTests
{
    private static BatteryCell Cell(int level, bool charging = false) => new(level, charging);

    [Fact]
    public void Silent_AboveTheThresholds()
    {
        Assert.Null(new LowBatteryAlerts().Check(Cell(25), Cell(85)));
    }

    [Fact]
    public void WarnsOnceAtEachThreshold()
    {
        var alerts = new LowBatteryAlerts();

        Assert.Equal(new LowBatteryAlert(20, null), alerts.Check(Cell(20), Cell(50)));
        Assert.Null(alerts.Check(Cell(20), Cell(50)));
        Assert.Null(alerts.Check(Cell(15), Cell(50)));
        Assert.Equal(new LowBatteryAlert(10, null), alerts.Check(Cell(10), Cell(50)));
        Assert.Null(alerts.Check(Cell(5), Cell(50)));
    }

    [Fact]
    public void ReportsBothBudsInOneAlert()
    {
        Assert.Equal(new LowBatteryAlert(20, 15), new LowBatteryAlerts().Check(Cell(20), Cell(15)));
    }

    [Fact]
    public void AStartBelowBothThresholdsWarnsOnce()
    {
        var alerts = new LowBatteryAlerts();

        Assert.Equal(new LowBatteryAlert(5, null), alerts.Check(Cell(5), null));
        Assert.Null(alerts.Check(Cell(5), null));
    }

    [Fact]
    public void AFlickerBetweenStepsDoesNotWarnAgain()
    {
        var alerts = new LowBatteryAlerts();
        alerts.Check(Cell(20), null);

        Assert.Null(alerts.Check(Cell(25), null));
        Assert.Null(alerts.Check(Cell(20), null));
    }

    [Fact]
    public void WarnsAgainAfterARealRecharge()
    {
        var alerts = new LowBatteryAlerts();
        alerts.Check(Cell(20), null);
        alerts.Check(Cell(80, charging: true), null);

        Assert.Equal(new LowBatteryAlert(20, null), alerts.Check(Cell(20), null));
    }

    [Fact]
    public void SilentWhileCharging()
    {
        var alerts = new LowBatteryAlerts();

        Assert.Null(alerts.Check(Cell(15, charging: true), null));
        Assert.Equal(new LowBatteryAlert(15, null), alerts.Check(Cell(15), null));
    }

    [Fact]
    public void AMissingBudIsNotAnAlert()
    {
        Assert.Null(new LowBatteryAlerts().Check(null, null));
    }
}

public class ProfileResolverTests
{
    private static readonly AppProfile Game = new("game", AncMode.Off, true);
    private static readonly AppProfile Chat = new("discord", AncMode.Transparency, null);
    private static readonly IReadOnlyList<AppProfile> Both = new[] { Game, Chat };

    private static HashSet<string> Running(params string[] names) => new(names, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Silent_WhileNothingMatches()
    {
        var resolver = new ProfileResolver();

        Assert.Null(resolver.Update(Both, Running("notepad"), AncMode.High, false));
        Assert.Null(resolver.Active);
    }

    [Fact]
    public void AppliesOnStart_RestoresOnExit()
    {
        var resolver = new ProfileResolver();

        Assert.Equal(new ProfileChange(AncMode.Off, true), resolver.Update(Both, Running("GAME"), AncMode.High, false));
        Assert.Equal(Game, resolver.Active);
        Assert.Null(resolver.Update(Both, Running("GAME"), AncMode.Off, true));
        Assert.Equal(new ProfileChange(AncMode.High, false), resolver.Update(Both, Running(), AncMode.Off, true));
        Assert.Null(resolver.Active);
    }

    [Fact]
    public void LeavesAloneWhatTheProfileDoesNotSet()
    {
        var resolver = new ProfileResolver();

        Assert.Equal(new ProfileChange(AncMode.Transparency, null), resolver.Update(Both, Running("discord"), AncMode.High, true));
        Assert.Equal(new ProfileChange(AncMode.High, null), resolver.Update(Both, Running(), AncMode.Transparency, true));
    }

    [Fact]
    public void DoesNotFightAManualChangeWhileActive()
    {
        var resolver = new ProfileResolver();
        resolver.Update(Both, Running("game"), AncMode.High, false);

        Assert.Null(resolver.Update(Both, Running("game"), AncMode.Transparency, true));
    }

    [Fact]
    public void AManualChangeSurvivesTheExit()
    {
        var resolver = new ProfileResolver();
        resolver.Update(Both, Running("game"), AncMode.High, false);

        // Noise control was changed by hand during the game, latency was not.
        Assert.Equal(new ProfileChange(null, false), resolver.Update(Both, Running(), AncMode.Transparency, true));
    }

    [Fact]
    public void AlreadyInTheWantedModeWritesNothing()
    {
        var resolver = new ProfileResolver();

        Assert.Null(resolver.Update(Both, Running("discord"), AncMode.Transparency, false));
        Assert.Null(resolver.Update(Both, Running(), AncMode.Transparency, false));
    }

    [Fact]
    public void TheFirstProfileInTheListWins()
    {
        var resolver = new ProfileResolver();
        resolver.Update(Both, Running("discord", "game"), AncMode.High, false);

        Assert.Equal(Game, resolver.Active);
    }

    [Fact]
    public void SwitchingProfilesKeepsTheOriginalBaseline()
    {
        var resolver = new ProfileResolver();
        resolver.Update(Both, Running("discord"), AncMode.High, false);

        Assert.Equal(new ProfileChange(AncMode.Off, true), resolver.Update(Both, Running("game"), AncMode.Transparency, false));
        Assert.Equal(new ProfileChange(AncMode.High, false), resolver.Update(Both, Running(), AncMode.Off, true));
    }

    [Fact]
    public void RemovingTheActiveProfileRestores()
    {
        var resolver = new ProfileResolver();
        resolver.Update(Both, Running("game"), AncMode.High, false);

        Assert.Equal(new ProfileChange(AncMode.High, false), resolver.Update(Array.Empty<AppProfile>(), Running("game"), AncMode.Off, true));
    }
}
