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
    private BudsPopup? _popup;
    private AppSettings? _settings;
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

        AppSettings settings = _settings = AppSettings.Load();
        Strings.Select(settings.Language);

        var state = _state = new BudsState();
        var session = _session = new BudsSession(state);
        _tray = new TrayIcon(state, settings, ToggleFlyout, () => _ = session.ToggleTransparencyAsync(), Quit);

        var hotkey = _hotkey = new GlobalHotkey();
        hotkey.Pressed += () => _ = session.ToggleTransparencyAsync();

        var profiles = _profiles = new ProfileRunner(state, session);

        var history = _history = new BatteryHistory(BatteryHistory.DefaultPath);
        history.Compact(DateTime.UtcNow);

        var alerts = new LowBatteryAlerts();
        var connectionPopup = new ConnectionPopupPolicy(DateTime.UtcNow);
        state.BatteryUpdated += () =>
        {
            history.Record(DateTime.UtcNow, state.Left, state.Right);
            bool arrived = connectionPopup.OnBattery(DateTime.UtcNow, state.Left is not null || state.Right is not null);
            if (alerts.Check(state.Left, state.Right) is { } alert)
            {
                // A warning must still get through when the card cannot show.
                if (CanShowPopup)
                    ShowPopup(BudsPopup.LowBattery(state, alert, OpenFlyout));
                else
                    _tray.Notify(state.Name, Labels.LowBattery(alert));
            }
            else if (arrived && settings.ConnectionPopup && CanShowPopup)
            {
                Log.Info("Showing the connection pop-up");
                ShowPopup(BudsPopup.Connected(state, OpenFlyout));
            }
        };

        _monitor = new DeviceMonitor();
        _monitor.BudsConnected += device =>
        {
            connectionPopup.OnConnected(DateTime.UtcNow);
            // Before the session changes the state: an open panel shows at once whether the hotkey is taken.
            hotkey.Register();
            session.Start(device);
            _tray.Visible = true;
            profiles.Start();
        };
        _monitor.BudsDisconnected += () =>
        {
            connectionPopup.OnDisconnected(DateTime.UtcNow);
            _popup?.Dismiss();
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

    private bool CanShowPopup => _flyout is null && !UserPresence.IsBusy;

    private void ShowPopup(BudsPopup popup)
    {
        _popup?.Dismiss();
        _popup = popup;
        popup.Closed += (_, _) =>
        {
            if (_popup == popup)
                _popup = null;
            if (_flyout is null)
                ProcessMemory.Trim();
        };
        popup.Open();
    }

    private void OpenFlyout()
    {
        // Both live in the same corner.
        _popup?.Dismiss();

        if (_flyout is null)
        {
            var flyout = _flyout = new Flyout(_state!, _session!, _history!, _profiles!, _hotkey!, _settings!, SetLanguage, Quit);
            flyout.Closed += (_, _) =>
            {
                if (_flyout == flyout)
                    _flyout = null;
                _flyoutClosedAt = DateTime.Now;
                ProcessMemory.Trim();
            };
        }
        _flyout.Open();
    }

    private void SetLanguage(AppLanguage language)
    {
        Strings.Select(language);
        _settings!.Language = language;
        _settings.Save();
        _tray?.RefreshTexts();

        // The panel is built with its texts. The new one opens first, so the app never loses the foreground.
        Flyout? old = _flyout;
        _flyout = null;
        OpenFlyout();
        old?.Dismiss();
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
        _popup?.Dismiss();
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
