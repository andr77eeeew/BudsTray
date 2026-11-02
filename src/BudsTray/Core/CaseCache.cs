using System;
using System.IO;
using System.Text.Json;

namespace BudsTray.Core;

/// <summary>
/// The case only reports its charge while the lid is open, so the last reading is kept on disk
/// and shown together with its age.
/// </summary>
public static class CaseCache
{
    private static string FilePath => Path.Combine(Log.DataDirectory, "case.json");

    public sealed record Entry(int Level, DateTime SeenAt);

    public static Entry? Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Entry>(File.ReadAllText(FilePath));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(Entry entry)
    {
        try
        {
            Directory.CreateDirectory(Log.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entry));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Could not save case cache: {e.Message}");
        }
    }
}
