using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BudsTray.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;

namespace BudsTray.Bluetooth;

/// <summary>
/// Waits for buds to connect, without polling and without touching the radio. Devices are told
/// apart by the vendor control service rather than by name, so a renamed pair still works.
/// Events fire on the thread the monitor was created on.
/// </summary>
public sealed class DeviceMonitor : IDisposable
{
    private readonly SynchronizationContext _context;
    private readonly Dictionary<string, BluetoothDevice> _paired = new();
    private readonly DeviceWatcher _watcher;
    private BluetoothDevice? _active;

    public DeviceMonitor()
    {
        _context = SynchronizationContext.Current ?? new SynchronizationContext();
        _watcher = DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
        _watcher.Added += (sender, info) => Post(() => _ = TrackAsync(info.Id));
        // Without a handler for every event the watcher does not report changes reliably.
        _watcher.Updated += (_, _) => { };
        _watcher.Removed += (_, update) => Post(() => Untrack(update.Id));
    }

    public event Action<BluetoothDevice>? BudsConnected;
    public event Action? BudsDisconnected;

    public void Start() => _watcher.Start();

    private async Task TrackAsync(string id)
    {
        if (_paired.ContainsKey(id))
            return;
        try
        {
            BluetoothDevice? device = await BluetoothDevice.FromIdAsync(id);
            if (device is null)
                return;

            _paired[id] = device;
            device.ConnectionStatusChanged += (_, _) => Post(() => _ = EvaluateAsync());
            await EvaluateAsync();
        }
        catch (Exception e)
        {
            Log.Info($"Could not track a paired device: {e.Message}");
        }
    }

    private void Untrack(string id)
    {
        if (!_paired.Remove(id, out BluetoothDevice? device))
            return;
        device.Dispose();
        if (ReferenceEquals(device, _active))
            Deactivate();
    }

    private async Task EvaluateAsync()
    {
        if (_active is { ConnectionStatus: BluetoothConnectionStatus.Connected })
            return;
        if (_active is not null)
            Deactivate();

        foreach (BluetoothDevice device in _paired.Values.ToList())
        {
            if (device.ConnectionStatus == BluetoothConnectionStatus.Connected && await HasControlServiceAsync(device))
            {
                // Another status change may have got here during the await.
                if (_active is not null || device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                    return;

                _active = device;
                Log.Info($"Buds connected: {device.Name}");
                BudsConnected?.Invoke(device);
                return;
            }
        }
    }

    private void Deactivate()
    {
        _active = null;
        Log.Info("Buds disconnected");
        BudsDisconnected?.Invoke();
    }

    private static async Task<bool> HasControlServiceAsync(BluetoothDevice device)
    {
        var id = RfcommServiceId.FromUuid(BudsSession.ControlService);
        var result = await device.GetRfcommServicesForIdAsync(id, BluetoothCacheMode.Cached);
        return result.Services.Count > 0;
    }

    private void Post(Action action) => _context.Post(_ => action(), null);

    public void Dispose()
    {
        _watcher.Stop();
        foreach (BluetoothDevice device in _paired.Values)
            device.Dispose();
        _paired.Clear();
    }
}
