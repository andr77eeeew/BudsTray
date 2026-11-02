using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BudsTray.Core;
using BudsTray.UI;
using Xunit;

namespace BudsTray.Tests;

public class LocalizationTests : IDisposable
{
    private static readonly PropertyInfo[] Texts = typeof(Strings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.PropertyType == typeof(string))
        .ToArray();

    public void Dispose() => Strings.Select(AppLanguage.English);

    [Fact]
    public void EveryLanguage_HasItsOwnStringsAndCode()
    {
        AppLanguage[] languages = Enum.GetValues<AppLanguage>();

        Assert.Equal(languages.Length, languages.Select(l => Strings.For(l)).Distinct().Count());
        Assert.Equal(languages.Length, languages.Select(l => Strings.For(l).LanguageCode).Distinct().Count());
    }

    [Fact]
    public void EveryLanguage_FillsEveryText()
    {
        foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
        {
            foreach (PropertyInfo text in Texts)
                Assert.False(string.IsNullOrWhiteSpace((string?)text.GetValue(Strings.For(language))), $"{language}.{text.Name}");
        }
    }

    [Fact]
    public void EveryLanguage_KeepsThePlaceholders()
    {
        foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
        {
            foreach (PropertyInfo text in Texts)
                Assert.True(Placeholders(Strings.For(AppLanguage.English), text).SequenceEqual(Placeholders(Strings.For(language), text)), $"{language}.{text.Name}");
        }
    }

    private static string[] Placeholders(Strings strings, PropertyInfo text) =>
        Regex.Matches((string)text.GetValue(strings)!, @"\{\d\}").Select(m => m.Value).Order().ToArray();

    [Theory]
    [InlineData(AppLanguage.English, "Low battery: left 20%, right 15%")]
    [InlineData(AppLanguage.Russian, "Низкий заряд: левый 20 %, правый 15 %")]
    public void LowBattery_UsesTheLanguage(AppLanguage language, string expected)
    {
        Strings.Select(language);

        Assert.Equal(expected, Labels.LowBattery(new LowBatteryAlert(20, 15)));
    }

    [Theory]
    [InlineData(AppLanguage.English, 20, null, "LEFT BUD LOW")]
    [InlineData(AppLanguage.English, null, 10, "RIGHT BUD LOW")]
    [InlineData(AppLanguage.English, 20, 15, "BOTH BUDS LOW")]
    [InlineData(AppLanguage.Russian, 20, null, "ЛЕВЫЙ РАЗРЯЖАЕТСЯ")]
    [InlineData(AppLanguage.Russian, null, 10, "ПРАВЫЙ РАЗРЯЖАЕТСЯ")]
    [InlineData(AppLanguage.Russian, 20, 15, "ОБА РАЗРЯЖАЮТСЯ")]
    public void LowBud_NamesTheBud(AppLanguage language, int? left, int? right, string expected)
    {
        Strings.Select(language);

        Assert.Equal(expected, Labels.LowBud(new LowBatteryAlert(left, right)));
    }

    [Theory]
    [InlineData(AppLanguage.English, 40, "≈ 40 min")]
    [InlineData(AppLanguage.English, 120, "≈ 2 h")]
    [InlineData(AppLanguage.English, 380, "≈ 6 h 20 min")]
    [InlineData(AppLanguage.Russian, 380, "≈ 6 ч 20 мин")]
    public void Remaining_UsesTheLanguage(AppLanguage language, int minutes, string expected)
    {
        Strings.Select(language);

        Assert.Equal(expected, Labels.Remaining(TimeSpan.FromMinutes(minutes)));
    }

    [Theory]
    [InlineData(AppLanguage.English, 0, "just now")]
    [InlineData(AppLanguage.English, 14, "14 min ago")]
    [InlineData(AppLanguage.English, 180, "3 h ago")]
    [InlineData(AppLanguage.English, 2880, "2 d ago")]
    [InlineData(AppLanguage.Russian, 14, "14 мин назад")]
    public void Age_UsesTheLanguage(AppLanguage language, int minutes, string expected)
    {
        Strings.Select(language);
        var now = new DateTime(2026, 11, 2, 12, 0, 0);

        Assert.Equal(expected, Labels.Age(now.AddMinutes(-minutes), now));
    }
}
