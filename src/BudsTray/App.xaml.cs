using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BudsTray.Bluetooth;
using BudsTray.Core;
using BudsTray.UI;

namespace BudsTray;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\BudsTray.Instance";
    private const string ShowEventName = @"Local\BudsTray.Show";

    // The tray click that closes an open panel deactivates it first; without this
    // the same click would immediately reopen it.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SettleBeforeTrim = TimeSpan.FromSeconds(15);

    private Mutex? _instance;
    private EventWaitHandle? _showRequest;
    private DeviceMonitor? _monitor;
    private TrayIcon? _tray;
    private GlobalHotkey? _hotkey;
    private BatteryHistory? _history;
    private ProfileRunner? _profiles;
    private BudsState? _state;
    private BudsSession? _session;
    private Flyout? _flyout;
    private DateTime _flyoutClosedAt = DateTime.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new Mutex(initiallyOwned: true, InstanceMutexName, out bool isFirst);
        if (!isFirst)
        {
            // The icon may be hidden, so launching the app again is how the panel is reached.
            // This launch holds the foreground right; without it the panel cannot take focus and never closes on click-away.
            AllowSetForegroundWindow(AnyProcess);
            using var request = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            request.Set();
            Shutdown();
            return;
        }

        var state = _state = new BudsState();
        var session = _session = new BudsSession(state);
        _tray = new TrayIcon(state, ToggleFlyout, () => _ = session.ToggleTransparencyAsync(), Quit);

        var hotkey = _hotkey = new GlobalHotkey();
        hotkey.Pressed += () => _ = session.ToggleTransparencyAsync();

        var profiles = _profiles = new ProfileRunner(state, session);

        var history = _history = new BatteryHistory(BatteryHistory.DefaultPath);
        history.Compact(DateTime.UtcNow);

        var alerts = new LowBatteryAlerts();
        state.BatteryUpdated += () =>
        {
            history.Record(DateTime.UtcNow, state.Left, state.Right);
            if (alerts.Check(state.Left, state.Right) is { } alert)
                _tray.Notify(state.Name, Labels.LowBattery(alert));
        };

        _monitor = new DeviceMonitor();
        _monitor.BudsConnected += device =>
        {
            session.Start(device);
            _tray.Visible = true;
            hotkey.Register();
            profiles.Start();
        };
        _monitor.BudsDisconnected += () =>
        {
            profiles.Stop();
            hotkey.Unregister();
            session.Stop();
            _tray.Visible = false;
        };
        _monitor.Start();

        ListenForShowRequests();

        if (!e.Args.Contains(AutoStart.BackgroundArgument))
            OpenFlyout();

        _ = TrimAfterStartupAsync();
    }

    private void ToggleFlyout()
    {
        if (_flyout is not null)
            _flyout.Dismiss();
        else if (DateTime.Now - _flyoutClosedAt > ReopenGuard)
            OpenFlyout();
    }

    private void OpenFlyout()
    {
        if (_flyout is null)
        {
            _flyout = new Flyout(_state!, _session!, _history!, _profiles!, _hotkey!, Quit);
            _flyout.Closed += (_, _) =>
            {
                _flyout = null;
                _flyoutClosedAt = DateTime.Now;
                ProcessMemory.Trim();
            };
        }
        _flyout.Open();
    }

    private async Task TrimAfterStartupAsync()
    {
        await Task.Delay(SettleBeforeTrim);
        if (_flyout is null)
            ProcessMemory.Trim();
    }

    private void ListenForShowRequests()
    {
        _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (_showRequest.WaitOne())
                Dispatcher.BeginInvoke(OpenFlyout);
        })
        {
            IsBackground = true,
            Name = "ShowRequestListener",
        };
        listener.Start();
    }

    private void Quit()
    {
        _profiles?.Stop();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _monitor?.Dispose();
        Shutdown();
    }

    private const int AnyProcess = -1;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
