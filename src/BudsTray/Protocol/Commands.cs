using System;
using System.Collections.Generic;
using System.Text;

namespace BudsTray.Protocol;

/// <summary>
/// Command ids. The high byte is the direction: C0 get, F0 set, 40 response, 70 ack, E0 event.
/// The low byte alone is ambiguous between directions, so the full 16-bit value is always used.
/// </summary>
public static class Command
{
    public const ushort GetBattery = 0xC007;
    public const ushort GetInEar = 0xC00E;
    public const ushort GetAnc = 0xC01E;
    public const ushort GetLatency = 0xC041;
    public const ushort GetFirmware = 0xC042;
    public const ushort GetBass = 0xC04E;
    public const ushort GetEq = 0xC050;

    public const ushort SetRing = 0xF002;
    public const ushort SetInEar = 0xF004;
    public const ushort SetAnc = 0xF00F;
    public const ushort SetEq = 0xF01D;
    public const ushort SetLatency = 0xF040;
    public const ushort SetBass = 0xF051;

    public const ushort ResponseBattery = 0x4007;
    public const ushort ResponseInEar = 0x400E;
    public const ushort ResponseAnc = 0x401E;
    public const ushort ResponseLatency = 0x4041;
    public const ushort ResponseFirmware = 0x4042;
    public const ushort ResponseBass = 0x404E;
    public const ushort ResponseEq = 0x4050;

    public const ushort EventBattery = 0xE001;
    public const ushort EventAnc = 0xE003;

    public static ushort ResponseTo(ushort get) => (ushort)(0x4000 | (get & 0x00FF));
    public static ushort AckTo(ushort set) => (ushort)(0x7000 | (set & 0x00FF));
    public static bool IsEvent(ushort command) => (command & 0xFF00) == 0xE000;
}

/// <summary>Wire values. 6 is absent from the enum on the device too.</summary>
public enum AncMode : byte
{
    High = 1,
    Mid = 2,
    Low = 3,
    Adaptive = 4,
    Off = 5,
    Transparency = 7,
}

/// <summary>Preset ids for CMF Buds Pro 2 (B172). Other models use a different table.</summary>
public enum EqPreset : byte
{
    Dirac = 0,
    Rock = 1,
    Electronic = 2,
    Pop = 3,
    Vocals = 4,
    Classical = 5,
    Custom = 6,
}

public enum Bud : byte
{
    Left = 2,
    Right = 3,
}

public readonly record struct BatteryCell(int Level, bool Charging);

public sealed record BatteryReport(BatteryCell? Left, BatteryCell? Right, BatteryCell? Case);

public static class Parse
{
    private const byte ComponentBoth = 1;
    private const byte ComponentLeft = 2;
    private const byte ComponentRight = 3;
    private const byte ComponentCase = 4;

    private const byte AncKindMode = 1;
    private const byte AncKindRememberedLevel = 2;
    private const byte InEarKeyDetection = 1;

    /// <summary>Count, then [component, value] pairs; value bit 7 is the charging flag.</summary>
    public static BatteryReport Battery(byte[] payload)
    {
        BatteryCell? left = null, right = null, @case = null;
        foreach ((byte component, byte value) in CountedPairs(payload))
        {
            int level = value & 0x7F;
            if (level > 100)
                continue;

            var cell = new BatteryCell(level, (value & 0x80) != 0);
            switch (component)
            {
                case ComponentBoth: left = cell; right = cell; break;
                case ComponentLeft: left = cell; break;
                case ComponentRight: right = cell; break;
                case ComponentCase: @case = cell; break;
            }
        }
        return new BatteryReport(left, right, @case);
    }

    /// <summary>The active mode. Used for both the response and the event, which only carries this record.</summary>
    public static AncMode? Anc(byte[] payload) => AncRecord(payload, AncKindMode);

    /// <summary>The strength the buds return to when noise cancelling comes back on.</summary>
    public static AncMode? AncRememberedLevel(byte[] payload) => AncRecord(payload, AncKindRememberedLevel);

    /// <summary>A list of 3-byte records [kind, value, 0].</summary>
    private static AncMode? AncRecord(byte[] payload, byte kind)
    {
        for (int i = 0; i + 2 < payload.Length; i += 3)
        {
            if (payload[i] == kind && Enum.IsDefined((AncMode)payload[i + 1]))
                return (AncMode)payload[i + 1];
        }
        return null;
    }

    public static EqPreset? Eq(byte[] payload) =>
        payload.Length >= 1 && Enum.IsDefined((EqPreset)payload[0]) ? (EqPreset)payload[0] : null;

    /// <summary>[enabled, level * 2].</summary>
    public static (bool Enabled, int Level)? Bass(byte[] payload) =>
        payload.Length >= 2 ? (payload[0] != 0, payload[1] / 2) : null;

    /// <summary>1 is on, 2 is off.</summary>
    public static bool? LowLatency(byte[] payload) =>
        payload.Length >= 1 ? payload[0] == 1 : null;

    /// <summary>Whether wear detection is enabled, not whether a bud is currently worn.</summary>
    public static bool? InEarDetection(byte[] payload)
    {
        foreach ((byte key, byte value) in CountedPairs(payload))
        {
            if (key == InEarKeyDetection)
                return value != 0;
        }
        return null;
    }

    public static string Firmware(byte[] payload) =>
        Encoding.ASCII.GetString(payload).Trim('\0', ' ');

    private static IEnumerable<(byte Key, byte Value)> CountedPairs(byte[] payload)
    {
        if (payload.Length == 0)
            yield break;

        int count = payload[0];
        for (int i = 0; i < count; i++)
        {
            int at = 1 + i * 2;
            if (at + 1 >= payload.Length)
                yield break;
            yield return (payload[at], payload[at + 1]);
        }
    }
}
