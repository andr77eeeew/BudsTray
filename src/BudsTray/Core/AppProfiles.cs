using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BudsTray.Protocol;

namespace BudsTray.Core;

/// <summary>What to set while an application runs. Null leaves that setting alone.</summary>
public sealed record AppProfile(string Process, AncMode? Anc, bool? LowLatency);

public sealed record ProfileChange(AncMode? Anc, bool? LowLatency);

public static class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string FilePath => Path.Combine(Log.DataDirectory, "profiles.json");

    public static List<AppProfile> Load()
    {
        try
        {
            return JsonSerializer.Deserialize<List<AppProfile>>(File.ReadAllText(FilePath), Options) ?? new List<AppProfile>();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<AppProfile>();
        }
    }

    public static void Save(IReadOnlyList<AppProfile> profiles)
    {
        try
        {
            Directory.CreateDirectory(Log.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(profiles, Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Could not save profiles: {e.Message}");
        }
    }
}

/// <summary>
/// Turns "which applications run" into changes to the buds. It acts only when the active profile
/// changes, so a setting the user changes by hand is not fought; and a setting they changed stays
/// as they left it when the profile ends.
/// </summary>
public sealed class ProfileResolver
{
    private Slot<AncMode> _anc;
    private Slot<bool> _latency;

    public AppProfile? Active { get; private set; }

    public ProfileChange? Update(IReadOnlyList<AppProfile> profiles, IReadOnlySet<string> running, AncMode anc, bool lowLatency)
    {
        AppProfile? next = profiles.FirstOrDefault(p => running.Contains(p.Process));
        if (next == Active)
            return null;

        Active = next;
        var change = new ProfileChange(_anc.Move(next?.Anc, anc), _latency.Move(next?.LowLatency, lowLatency));
        return change is { Anc: null, LowLatency: null } ? null : change;
    }

    private struct Slot<T> where T : struct
    {
        private T? _before, _applied;

        public T? Move(T? wanted, T current)
        {
            if (_applied is { } ours && !Same(current, ours))
            {
                _before = null;
                _applied = null;
            }

            if (wanted is { } value)
            {
                _before ??= current;
                _applied = value;
                return Same(current, value) ? null : value;
            }

            T? back = _applied is null ? null : _before;
            _before = null;
            _applied = null;
            return back is { } b && !Same(b, current) ? b : null;
        }

        private static bool Same(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);
    }
}
