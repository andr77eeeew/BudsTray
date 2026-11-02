using System;
using System.Collections.Generic;

namespace BudsTray.Protocol;

public static class Crc16
{
    /// <summary>CRC-16/MODBUS: reflected, poly 0xA001, init 0xFFFF.</summary>
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }
}

public sealed record Frame(ushort Command, byte Sequence, byte[] Payload);

/// <summary>
/// Layout: 55 | control (2) | command LE (2) | length LE (2) | sequence | payload | CRC LE (2, optional).
/// Control bit 0x20 of the first byte says whether the CRC trailer is present.
/// </summary>
public static class FrameCodec
{
    public const int HeaderSize = 8;
    private const byte StartOfFrame = 0x55;
    private const byte ControlWithCrc = 0x60;
    private const byte ControlHasCrcBit = 0x20;
    private const byte DeviceTypeTws = 0x01;

    public static byte[] Encode(ushort command, byte sequence, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[HeaderSize + payload.Length + 2];
        frame[0] = StartOfFrame;
        frame[1] = ControlWithCrc;
        frame[2] = DeviceTypeTws;
        frame[3] = (byte)command;
        frame[4] = (byte)(command >> 8);
        frame[5] = (byte)payload.Length;
        frame[6] = (byte)(payload.Length >> 8);
        frame[7] = sequence;
        payload.CopyTo(frame.AsSpan(HeaderSize));

        // Requests are checksummed over the whole frame, unlike responses (see FrameReader).
        ushort crc = Crc16.Compute(frame.AsSpan(0, HeaderSize + payload.Length));
        frame[^2] = (byte)crc;
        frame[^1] = (byte)(crc >> 8);
        return frame;
    }

    internal static bool IsStart(byte value) => value == StartOfFrame;
    internal static bool HasCrc(byte control) => (control & ControlHasCrcBit) != 0;
    internal static bool IsDeviceType(byte value) => value == DeviceTypeTws;
}

/// <summary>Reassembles frames from the RFCOMM byte stream and resynchronises on corruption.</summary>
public sealed class FrameReader
{
    private const int MaxPayload = 1024;
    private readonly List<byte> _buffer = new();

    public IReadOnlyList<Frame> Feed(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            _buffer.Add(b);

        var frames = new List<Frame>();
        while (TryTake(out Frame? frame))
        {
            if (frame is not null)
                frames.Add(frame);
        }
        return frames;
    }

    /// <returns>False once the buffer has to wait for more bytes.</returns>
    private bool TryTake(out Frame? frame)
    {
        frame = null;

        int start = _buffer.FindIndex(FrameCodec.IsStart);
        if (start < 0)
        {
            _buffer.Clear();
            return false;
        }
        _buffer.RemoveRange(0, start);

        if (_buffer.Count < FrameCodec.HeaderSize)
            return false;

        int length = _buffer[5] | (_buffer[6] << 8);
        bool hasCrc = FrameCodec.HasCrc(_buffer[1]);
        if (length > MaxPayload || !FrameCodec.IsDeviceType(_buffer[2]))
            return DropStartByte();

        int total = FrameCodec.HeaderSize + length + (hasCrc ? 2 : 0);
        if (_buffer.Count < total)
            return false;

        byte[] raw = _buffer.GetRange(0, total).ToArray();
        byte[] payload = raw.AsSpan(FrameCodec.HeaderSize, length).ToArray();
        ushort command = (ushort)(raw[3] | (raw[4] << 8));

        if (hasCrc ? !CrcMatches(raw, payload) : !Command.IsEvent(command))
            return DropStartByte();

        _buffer.RemoveRange(0, total);
        frame = new Frame(command, raw[7], payload);
        return true;
    }

    private bool DropStartByte()
    {
        _buffer.RemoveAt(0);
        return true;
    }

    // CMF Buds Pro 2 answers with a CRC over the payload only (verified on firmware 1.0.1.74);
    // Nothing Ear (a) covers the whole frame. Accept either.
    private static bool CrcMatches(byte[] raw, byte[] payload)
    {
        ushort expected = (ushort)(raw[^2] | (raw[^1] << 8));
        return Crc16.Compute(payload) == expected || Crc16.Compute(raw.AsSpan(0, raw.Length - 2)) == expected;
    }
}
