using System;
using System.Linq;
using BudsTray.Protocol;
using Xunit;

namespace BudsTray.Tests;

/// <summary>Payloads below were captured from real CMF Buds Pro 2, firmware 1.0.1.74.</summary>
public class ProtocolTests
{
    private static byte[] Hex(string text) => Convert.FromHexString(text.Replace(" ", ""));

    private static byte[] Response(ushort command, byte sequence, string payloadHex)
    {
        byte[] payload = Hex(payloadHex);
        ushort crc = Crc16.Compute(payload);
        return new byte[] { 0x55, 0x60, 0x01, (byte)command, (byte)(command >> 8), (byte)payload.Length, 0, sequence }
            .Concat(payload)
            .Concat(new[] { (byte)crc, (byte)(crc >> 8) })
            .ToArray();
    }

    [Fact]
    public void Encode_ProducesFrameWithCrcOverWholeFrame()
    {
        byte[] frame = FrameCodec.Encode(Command.GetBattery, 1, ReadOnlySpan<byte>.Empty);

        Assert.Equal(Hex("55 60 01 07 C0 00 00 01"), frame[..8]);
        ushort crc = Crc16.Compute(frame.AsSpan(0, 8));
        Assert.Equal((byte)crc, frame[8]);
        Assert.Equal((byte)(crc >> 8), frame[9]);
    }

    [Fact]
    public void Encode_IncludesPayloadLength()
    {
        byte[] frame = FrameCodec.Encode(Command.SetAnc, 9, new byte[] { 1, (byte)AncMode.Off, 0 });

        Assert.Equal(13, frame.Length);
        Assert.Equal(3, frame[5]);
        Assert.Equal(new byte[] { 1, 5, 0 }, frame[8..11]);
    }

    [Fact]
    public void Reader_AcceptsResponseWithCrcOverPayloadOnly()
    {
        var frames = new FrameReader().Feed(Response(0x4007, 1, "02 02 5f 03 5a"));

        var frame = Assert.Single(frames);
        Assert.Equal(0x4007, frame.Command);
        Assert.Equal(1, frame.Sequence);
        Assert.Equal(Hex("02 02 5f 03 5a"), frame.Payload);
    }

    [Fact]
    public void Reader_AcceptsEmptyResponse()
    {
        var frame = Assert.Single(new FrameReader().Feed(Response(0x4040, 6, "")));

        Assert.Empty(frame.Payload);
    }

    [Fact]
    public void Reader_AcceptsFrameWithCrcOverWholeFrame()
    {
        var frame = Assert.Single(new FrameReader().Feed(FrameCodec.Encode(0x4007, 2, new byte[] { 1, 2, 3 })));

        Assert.Equal(new byte[] { 1, 2, 3 }, frame.Payload);
    }

    [Fact]
    public void Reader_AcceptsEventWithoutCrc()
    {
        byte[] raw = Hex("55 00 01 03 E0 02 00 00 01 07");

        var frame = Assert.Single(new FrameReader().Feed(raw));

        Assert.Equal(Command.EventAnc, frame.Command);
    }

    [Fact]
    public void Reader_ReassemblesFrameSplitAcrossReads()
    {
        byte[] raw = Response(0x4042, 8, "31 2e 30 2e 31 2e 37 34");
        var reader = new FrameReader();

        Assert.Empty(reader.Feed(raw.AsSpan(0, 5)));
        Assert.Empty(reader.Feed(raw.AsSpan(5, 6)));
        var frame = Assert.Single(reader.Feed(raw.AsSpan(11)));

        Assert.Equal("1.0.1.74", Parse.Firmware(frame.Payload));
    }

    [Fact]
    public void Reader_SplitsSeveralFramesFromOneRead()
    {
        byte[] raw = Response(0x4050, 12, "00").Concat(Response(0x404E, 11, "01 02")).ToArray();

        var frames = new FrameReader().Feed(raw);

        Assert.Equal(new ushort[] { 0x4050, 0x404E }, frames.Select(f => f.Command));
    }

    [Fact]
    public void Reader_ResynchronisesAfterGarbage()
    {
        byte[] raw = new byte[] { 0x00, 0x55, 0x12 }.Concat(Response(0x4050, 12, "03")).ToArray();

        var frame = Assert.Single(new FrameReader().Feed(raw));

        Assert.Equal(new byte[] { 3 }, frame.Payload);
    }

    [Fact]
    public void Reader_RejectsCorruptedCrc()
    {
        byte[] raw = Response(0x4050, 12, "03");
        raw[^1] ^= 0xFF;

        Assert.Empty(new FrameReader().Feed(raw));
    }

    [Fact]
    public void Battery_ParsesRealCapture()
    {
        BatteryReport report = Parse.Battery(Hex("02 02 5f 03 5a"));

        Assert.Equal(new BatteryCell(95, false), report.Left);
        Assert.Equal(new BatteryCell(90, false), report.Right);
        Assert.Null(report.Case);
    }

    [Fact]
    public void Battery_ReadsChargingFlagAndCase()
    {
        BatteryReport report = Parse.Battery(Hex("03 02 E4 03 64 04 C6"));

        Assert.Equal(new BatteryCell(100, true), report.Left);
        Assert.Equal(new BatteryCell(100, false), report.Right);
        Assert.Equal(new BatteryCell(70, true), report.Case);
    }

    [Fact]
    public void Battery_SharedComponentFillsBothBuds()
    {
        BatteryReport report = Parse.Battery(Hex("01 01 32"));

        Assert.Equal(new BatteryCell(50, false), report.Left);
        Assert.Equal(new BatteryCell(50, false), report.Right);
    }

    [Fact]
    public void Battery_IgnoresImpossibleLevelAndTruncatedPayload()
    {
        BatteryReport report = Parse.Battery(Hex("03 02 65 03"));

        Assert.Null(report.Left);
        Assert.Null(report.Right);
    }

    [Fact]
    public void Anc_ReadsModeFromRealCaptureNotTheLevelRecord()
    {
        // The user confirmed the buds were in Transparency when this was captured.
        Assert.Equal(AncMode.Transparency, Parse.Anc(Hex("02 01 00 01 07 00")));
    }

    [Fact]
    public void Anc_ReadsRememberedLevelSeparatelyFromMode()
    {
        Assert.Equal(AncMode.Adaptive, Parse.AncRememberedLevel(Hex("02 04 00 01 07 00")));
    }

    [Fact]
    public void Anc_EventFromRealCaptureCarriesOnlyTheMode()
    {
        byte[] raw = Hex("55 00 01 03 E0 03 00 00 01 05 00");

        var frame = Assert.Single(new FrameReader().Feed(raw));

        Assert.Equal(Command.EventAnc, frame.Command);
        Assert.Equal(AncMode.Off, Parse.Anc(frame.Payload));
        Assert.Null(Parse.AncRememberedLevel(frame.Payload));
    }

    [Fact]
    public void Anc_UnknownModeIsNull()
    {
        Assert.Null(Parse.Anc(Hex("01 06 00")));
    }

    [Fact]
    public void Bass_LevelIsHalfOfWireValue()
    {
        Assert.Equal((true, 1), Parse.Bass(Hex("01 02")));
    }

    [Fact]
    public void LowLatency_TwoMeansOff()
    {
        Assert.False(Parse.LowLatency(Hex("02")));
        Assert.True(Parse.LowLatency(Hex("01")));
    }

    [Fact]
    public void InEar_ReadsDetectionFromRealCapture()
    {
        byte[] payload = Hex("0e 01 01 02 00 03 01 07 01 09 01 0a 00 0b 00 0c 01 0e 01 0f 01 12 01 15 01 16 01 18 01");

        Assert.True(Parse.InEarDetection(payload));
    }

    [Fact]
    public void Eq_ReadsPreset()
    {
        Assert.Equal(EqPreset.Dirac, Parse.Eq(Hex("00")));
        Assert.Null(Parse.Eq(Hex("09")));
    }

    [Theory]
    [InlineData(Command.GetBattery, 0x4007)]
    [InlineData(Command.GetFirmware, 0x4042)]
    public void ResponseTo_KeepsOpcode(ushort get, ushort response)
    {
        Assert.Equal(response, Command.ResponseTo(get));
    }

    [Fact]
    public void AckTo_KeepsOpcode()
    {
        Assert.Equal(0x7002, Command.AckTo(Command.SetRing));
    }
}
