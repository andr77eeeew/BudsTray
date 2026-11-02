using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BudsTray.Core;
using BudsTray.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace BudsTray.Bluetooth;

/// <summary>
/// Holds the control channel open for as long as the buds stay connected, so that changes made on
/// the buds themselves (a pinch, the phone app) reach the panel and the tray icon straight away.
/// </summary>
public sealed class BudsSession
{
    /// <summary>Nothing's vendor service. The Serial Port service the buds also offer does not answer these frames.</summary>
    public static readonly Guid ControlService = Guid.Parse("aeac4a03-dff5-498f-843a-34487cf133eb");

    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan BatteryPollInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SettleBeforeReadBack = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan AncSettleTime = TimeSpan.FromMilliseconds(2200);
    private static readonly int[] RetryDelaysSeconds = { 1, 2, 5, 10, 30 };

    private readonly BudsState _state;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly ConcurrentDictionary<ushort, TaskCompletionSource<Frame>> _pending = new();

    private CancellationTokenSource? _lifetime;
    private Stream? _output;
    private byte _sequence;
    private DateTime _ancSettlesAt = DateTime.MinValue;

    public BudsSession(BudsState state) => _state = state;

    /// <summary>True once the settings have been read from the buds, so that the state can be trusted as a baseline.</summary>
    public bool SettingsKnown { get; private set; }

    public void Start(BluetoothDevice device)
    {
        Stop();
        _state.Name = device.Name;
        _state.Link = LinkState.Connecting;
        _lifetime = new CancellationTokenSource();
        _ = RunAsync(device, _lifetime.Token);
    }

    public void Stop()
    {
        _lifetime?.Cancel();
        _lifetime = null;
        _state.Link = LinkState.Disconnected;
        _state.MarkOffline();
    }

    public async Task RefreshAllAsync()
    {
        if (_state.Link != LinkState.Online)
            return;

        await ReadAsync(Command.GetBattery);
        await ReadAsync(Command.GetAnc);
        if (_state.SupportsEq)
        {
            await ReadAsync(Command.GetEq);
            await ReadAsync(Command.GetBass);
        }
        await ReadAsync(Command.GetLatency);
        await ReadAsync(Command.GetInEar);
        await ReadAsync(Command.GetFirmware);
    }

    /// <summary>
    /// The buds acknowledge at once but apply at most one change per ~1.5 s, deferring the rest
    /// (last one wins) and announcing what they applied with an event. Reading the mode back
    /// straight away would return the old one, so the panel keeps showing the request until the
    /// cool-down has passed, and only then checks.
    /// </summary>
    public async Task SetAncAsync(AncMode mode)
    {
        _state.Anc = mode;
        if (mode is AncMode.Low or AncMode.Mid or AncMode.High or AncMode.Adaptive)
            _state.AncLevel = mode;
        DateTime settlesAt = DateTime.UtcNow + AncSettleTime;
        _ancSettlesAt = settlesAt;
        await WriteAsync(Command.SetAnc, new byte[] { 1, (byte)mode, 0 }, readBack: null);

        await Task.Delay(AncSettleTime);
        if (_ancSettlesAt == settlesAt)
            await ReadAsync(Command.GetAnc);
    }

    /// <summary>For when someone starts talking to you: to transparency, and back to noise cancelling.</summary>
    public Task ToggleTransparencyAsync()
    {
        if (_state.Link != LinkState.Online)
            return Task.CompletedTask;
        return SetAncAsync(_state.Anc == AncMode.Transparency ? _state.AncLevel : AncMode.Transparency);
    }

    public Task SetEqAsync(EqPreset preset)
    {
        _state.Eq = preset;
        return WriteAsync(Command.SetEq, new byte[] { (byte)preset, 0 }, Command.GetEq);
    }

    public Task SetBassAsync(bool enabled, int level)
    {
        _state.BassEnabled = enabled;
        _state.BassLevel = level;
        return WriteAsync(Command.SetBass, new byte[] { (byte)(enabled ? 1 : 0), (byte)(level * 2) }, Command.GetBass);
    }

    public Task SetLowLatencyAsync(bool on)
    {
        _state.LowLatency = on;
        return WriteAsync(Command.SetLatency, new byte[] { (byte)(on ? 1 : 2), 0 }, Command.GetLatency);
    }

    public Task SetInEarDetectionAsync(bool on)
    {
        _state.InEarDetection = on;
        return WriteAsync(Command.SetInEar, new byte[] { 1, 1, (byte)(on ? 1 : 0) }, Command.GetInEar);
    }

    public Task RingAsync(Bud bud, bool on) =>
        WriteAsync(Command.SetRing, new byte[] { (byte)bud, (byte)(on ? 1 : 0) }, readBack: null);

    private async Task RunAsync(BluetoothDevice device, CancellationToken token)
    {
        int attempt = 0;
        var serviceId = RfcommServiceId.FromUuid(ControlService);

        while (!token.IsCancellationRequested && device.ConnectionStatus == BluetoothConnectionStatus.Connected)
        {
            StreamSocket? socket = null;
            try
            {
                // Cached only: an uncached lookup could page buds that have just left for the phone.
                var services = await device.GetRfcommServicesForIdAsync(serviceId, BluetoothCacheMode.Cached);
                RfcommDeviceService service = services.Services.FirstOrDefault()
                    ?? throw new IOException($"Control service not found: {services.Error}");

                socket = new StreamSocket();
                await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, service.ProtectionLevel);
                token.ThrowIfCancellationRequested();

                _output = socket.OutputStream.AsStreamForWrite(0);
                Stream input = socket.InputStream.AsStreamForRead(0);
                _state.Link = LinkState.Online;
                attempt = 0;
                Log.Info($"Control channel open: {device.Name}");

                // A cancelled session closes the socket, which ends the pump.
                using CancellationTokenRegistration closeOnCancel = token.Register(socket.Dispose);
                _ = ReadSettingsAsync();
                _ = PollBatteryAsync(token);
                await PumpAsync(input, token);
                Log.Info("Control channel closed by the buds");
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                Log.Info($"Control channel failed: {e.GetType().Name} 0x{e.HResult:X8}: {e.Message}");
            }
            catch (Exception)
            {
                return;
            }
            finally
            {
                _output = null;
                SettingsKnown = false;
                socket?.Dispose();
                FailPending();
            }

            if (token.IsCancellationRequested)
                return;

            _state.Link = LinkState.Connecting;
            _state.MarkOffline();
            int delay = RetryDelaysSeconds[Math.Min(attempt++, RetryDelaysSeconds.Length - 1)];
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delay), token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReadSettingsAsync()
    {
        await RefreshAllAsync();
        SettingsKnown = _output is not null;
    }

    private async Task PumpAsync(Stream input, CancellationToken token)
    {
        var reader = new FrameReader();
        var buffer = new byte[1024];
        while (!token.IsCancellationRequested)
        {
            int read = await input.ReadAsync(buffer);
            if (read == 0)
                return;
            foreach (Frame frame in reader.Feed(buffer.AsSpan(0, read)))
                Handle(frame);
        }
    }

    private async Task PollBatteryAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(BatteryPollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
                await ReadAsync(Command.GetBattery);
        }
        catch (OperationCanceledException)
        {
            // Session ended.
        }
    }

    private void Handle(Frame frame)
    {
        Apply(frame);
        if (_pending.TryRemove(frame.Command, out TaskCompletionSource<Frame>? waiter))
            waiter.TrySetResult(frame);
    }

    private void Apply(Frame frame)
    {
        byte[] p = frame.Payload;
        switch (frame.Command)
        {
            case Command.ResponseBattery:
                _state.ApplyBattery(Parse.Battery(p));
                break;
            case Command.ResponseAnc:
                if (Parse.AncRememberedLevel(p) is { } level)
                    _state.AncLevel = level;
                if (DateTime.UtcNow >= _ancSettlesAt && Parse.Anc(p) is { } anc)
                    _state.Anc = anc;
                break;
            case Command.EventAnc:
                // A deferred request can still be on its way, and that one is the state to show.
                if (DateTime.UtcNow >= _ancSettlesAt && Parse.Anc(p) is { } applied)
                    _state.Anc = applied;
                break;
            case Command.ResponseEq:
                if (Parse.Eq(p) is { } eq)
                    _state.Eq = eq;
                break;
            case Command.ResponseBass:
                if (Parse.Bass(p) is { } bass)
                {
                    _state.BassEnabled = bass.Enabled;
                    _state.BassLevel = Math.Clamp(bass.Level, 1, 5);
                }
                break;
            case Command.ResponseLatency:
                if (Parse.LowLatency(p) is { } latency)
                    _state.LowLatency = latency;
                break;
            case Command.ResponseInEar:
                if (Parse.InEarDetection(p) is { } inEar)
                    _state.InEarDetection = inEar;
                break;
            case Command.ResponseFirmware:
                _state.Firmware = Parse.Firmware(p);
                break;

            // The layout of this event has not been captured on this model, so it only says "ask again".
            case Command.EventBattery:
                _ = ReadAsync(Command.GetBattery);
                break;
        }
    }

    private Task ReadAsync(ushort get) =>
        RequestAsync(get, Array.Empty<byte>(), Command.ResponseTo(get));

    private async Task WriteAsync(ushort set, byte[] payload, ushort? readBack)
    {
        try
        {
            await RequestAsync(set, payload, Command.AckTo(set));
            if (readBack is { } get)
            {
                await Task.Delay(SettleBeforeReadBack);
                await ReadAsync(get);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            Log.Info($"Write {set:X4} failed: {e.Message}");
        }
    }

    /// <summary>One request at a time; a missing reply is not an error, the state just stays as it was.</summary>
    private async Task RequestAsync(ushort command, byte[] payload, ushort reply)
    {
        await _requestGate.WaitAsync();
        try
        {
            Stream? output = _output;
            if (output is null)
                return;

            var waiter = new TaskCompletionSource<Frame>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[reply] = waiter;
            try
            {
                await output.WriteAsync(FrameCodec.Encode(command, NextSequence(), payload));
                await output.FlushAsync();
                await waiter.Task.WaitAsync(ReplyTimeout);
            }
            catch (TimeoutException)
            {
                Log.Info($"No reply to {command:X4}");
            }
            catch (TaskCanceledException)
            {
                // The channel went away while waiting.
            }
            finally
            {
                _pending.TryRemove(reply, out _);
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private void FailPending()
    {
        foreach (ushort key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out TaskCompletionSource<Frame>? waiter))
                waiter.TrySetCanceled();
        }
    }

    // 0 is what the buds use on their own events, so requests stay out of it.
    private byte NextSequence()
    {
        _sequence = _sequence >= 250 ? (byte)1 : (byte)(_sequence + 1);
        return _sequence;
    }
}
