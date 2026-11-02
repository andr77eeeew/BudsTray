using System;
using System.IO;
using System.Text.Json;

namespace BudsTray.Core;

public enum AppLanguage
{
    English,
    Russian,
}

/// <summary>The user's choices. Anything missing or unreadable in the file keeps its default.</summary>
public sealed class AppSettings
{
    private static string FilePath => Path.Combine(Log.DataDirectory, "settings.json");

    public AppLanguage Language { get; set; } = AppLanguage.English;
    public bool ConnectionPopup { get; set; } = true;

    public static AppSettings Load()
    {
        try
        {
            return Parse(File.ReadAllText(FilePath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public static AppSettings Parse(string? json)
    {
        var settings = new AppSettings();
        if (string.IsNullOrWhiteSpace(json))
            return settings;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return settings;

            JsonElement root = document.RootElement;
            if (root.TryGetProperty(nameof(Language), out JsonElement language)
                && language.ValueKind == JsonValueKind.String
                && Enum.TryParse(language.GetString(), ignoreCase: true, out AppLanguage parsed)
                && Enum.IsDefined(parsed))
            {
                settings.Language = parsed;
            }
            if (root.TryGetProperty(nameof(ConnectionPopup), out JsonElement popup)
                && popup.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                settings.ConnectionPopup = popup.GetBoolean();
            }
        }
        catch (JsonException)
        {
        }
        return settings;
    }

    public string Serialize() => JsonSerializer.Serialize(new { Language = Language.ToString(), ConnectionPopup });

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Log.DataDirectory);
            File.WriteAllText(FilePath, Serialize());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Could not save settings: {e.Message}");
        }
    }
}
