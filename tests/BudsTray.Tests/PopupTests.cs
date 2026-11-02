using System;
using System.Linq;
using BudsTray.Core;
using BudsTray.UI;
using Xunit;

namespace BudsTray.Tests;

public class AppSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("{}")]
    public void Parse_FallsBackToDefaults(string? json)
    {
        AppSettings settings = AppSettings.Parse(json);

        Assert.Equal(AppLanguage.English, settings.Language);
        Assert.True(settings.ConnectionPopup);
    }

    [Fact]
    public void Parse_KeepsWhatItCanRead()
    {
        AppSettings settings = AppSettings.Parse("""{ "Language": "Klingon", "ConnectionPopup": false }""");

        Assert.Equal(AppLanguage.English, settings.Language);
        Assert.False(settings.ConnectionPopup);
    }

    [Fact]
    public void Parse_IgnoresValuesOfTheWrongType()
    {
        AppSettings settings = AppSettings.Parse("""{ "Language": 1, "ConnectionPopup": "no" }""");

        Assert.Equal(AppLanguage.English, settings.Language);
        Assert.True(settings.ConnectionPopup);
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var settings = new AppSettings { Language = AppLanguage.Russian, ConnectionPopup = false };

        AppSettings read = AppSettings.Parse(settings.Serialize());

        Assert.Equal(AppLanguage.Russian, read.Language);
        Assert.False(read.ConnectionPopup);
    }
}

public class ConnectionPopupPolicyTests
{
    private static readonly DateTime Started = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime After(int seconds) => Started.AddSeconds(seconds);

    [Fact]
    public void BudsAlreadyConnectedAtStartup_AreNotAnnounced()
    {
        var policy = new ConnectionPopupPolicy(Started);

        policy.OnConnected(After(1));

        Assert.False(policy.OnBattery(After(2), chargeKnown: true));
    }

    [Fact]
    public void ArrivingBuds_AreAnnouncedOnce()
    {
        var policy = new ConnectionPopupPolicy(Started);

        policy.OnConnected(After(60));

        Assert.True(policy.OnBattery(After(62), chargeKnown: true));
        Assert.False(policy.OnBattery(After(63), chargeKnown: true));
    }

    [Fact]
    public void WaitsUntilTheChargeIsKnown()
    {
        var policy = new ConnectionPopupPolicy(Started);

        policy.OnConnected(After(60));

        Assert.False(policy.OnBattery(After(61), chargeKnown: false));
        Assert.True(policy.OnBattery(After(62), chargeKnown: true));
    }

    [Fact]
    public void GivesUpOnALateReport()
    {
        var policy = new ConnectionPopupPolicy(Started);

        policy.OnConnected(After(60));

        Assert.False(policy.OnBattery(After(95), chargeKnown: true));
        Assert.False(policy.OnBattery(After(96), chargeKnown: true));
    }

    [Fact]
    public void Disconnecting_CancelsTheWait()
    {
        var policy = new ConnectionPopupPolicy(Started);

        policy.OnConnected(After(60));
        policy.OnDisconnected(After(61));

        Assert.False(policy.OnBattery(After(62), chargeKnown: true));
    }

    [Fact]
    public void BriefDropout_IsNotAnnounced()
    {
        var policy = new ConnectionPopupPolicy(Started);
        policy.OnConnected(After(60));
        policy.OnBattery(After(61), chargeKnown: true);

        policy.OnDisconnected(After(100));
        policy.OnConnected(After(110));

        Assert.False(policy.OnBattery(After(111), chargeKnown: true));
    }

    [Fact]
    public void ComingBackLater_IsAnnouncedAgain()
    {
        var policy = new ConnectionPopupPolicy(Started);
        policy.OnConnected(After(60));
        policy.OnBattery(After(61), chargeKnown: true);

        policy.OnDisconnected(After(100));
        policy.OnConnected(After(200));

        Assert.True(policy.OnBattery(After(201), chargeKnown: true));
    }
}

public class BudsGlyphTests
{
    [Theory]
    [InlineData(BudsShape.LeftBud)]
    [InlineData(BudsShape.RightBud)]
    [InlineData(BudsShape.Case)]
    public void Rows_AreRectangularAndUseKnownTones(BudsShape shape)
    {
        string[] rows = BudsGlyph.Rows(shape);

        Assert.All(rows, row => Assert.Equal(rows[0].Length, row.Length));
        Assert.All(rows, row => Assert.All(row, tone => Assert.Contains(tone, ".#+-")));
    }

    [Fact]
    public void RightBud_MirrorsTheLeft()
    {
        string[] left = BudsGlyph.Rows(BudsShape.LeftBud);
        string[] right = BudsGlyph.Rows(BudsShape.RightBud);

        Assert.Equal(left, right.Select(row => new string(row.Reverse().ToArray())));
    }
}
