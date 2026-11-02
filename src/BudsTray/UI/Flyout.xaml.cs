using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BudsTray.Bluetooth;
using BudsTray.Core;
using BudsTray.Protocol;
using Forms = System.Windows.Forms;

namespace BudsTray.UI;

public partial class Flyout : Window
{
    private static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan DayRange = TimeSpan.FromDays(1);
    private static readonly TimeSpan WeekRange = TimeSpan.FromDays(7);

    private readonly BudsState _state;
    private readonly BudsSession _session;
    private readonly BatteryHistory _history;
    private readonly ProfileRunner _profiles;
    private readonly GlobalHotkey _hotkey;
    private readonly Action _quit;

    private readonly Dictionary<AncMode, ToggleButton> _ancLevels = new();
    private readonly Dictionary<EqPreset, ToggleButton> _eqPresets = new();
    private readonly List<ToggleButton> _bassLevels = new();
    private ToggleButton _ancOn = null!, _ancTransparency = null!, _ancOff = null!;
    private ToggleButton _ringLeft = null!, _ringRight = null!, _ringStop = null!;
    private ToggleButton _rangeDay = null!, _rangeWeek = null!;

    private List<BatterySample> _samples = new();
    private TimeSpan _range = DayRange;

    private Bud? _armedRing;
    private Bud? _ringing;
    private bool _dismissing;

    public Flyout(BudsState state, BudsSession session, BatteryHistory history, ProfileRunner profiles, GlobalHotkey hotkey, Action quit)
    {
        _state = state;
        _session = session;
        _history = history;
        _profiles = profiles;
        _hotkey = hotkey;
        _quit = quit;

        InitializeComponent();
        BuildSegments();
        ProfilesSection.Attach(state, profiles);

        // One section at a time, or the panel outgrows a small screen.
        ToggleButton[] disclosures = { EqDisclosure, HistoryDisclosure, ProfilesDisclosure, FindDisclosure };
        foreach (ToggleButton disclosure in disclosures)
        {
            disclosure.Click += (_, _) =>
            {
                foreach (ToggleButton other in disclosures)
                {
                    if (other != disclosure)
                        other.IsChecked = false;
                }
                Disarm();
                Render();
            };
        }

        _state.PropertyChanged += OnStateChanged;
        _state.BatteryUpdated += OnBatteryUpdated;
        _profiles.Changed += Render;

        Deactivated += (_, _) => Dismiss();
        Closed += (_, _) =>
        {
            _state.PropertyChanged -= OnStateChanged;
            _state.BatteryUpdated -= OnBatteryUpdated;
            _profiles.Changed -= Render;
            ProfilesSection.Detach();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Dismiss();
        };
        SizeChanged += (_, _) => Place();
        LoadHistory();
        Render();
    }

    public void Open()
    {
        MaxHeight = SystemParameters.WorkArea.Height;
        Show();
        Place();
        Activate();
        PlayOpenAnimation();
        _ = _session.RefreshAllAsync();
    }

    /// <summary>The window is rebuilt on every open, so that nothing but the Bluetooth watch stays in memory.</summary>
    public void Dismiss()
    {
        if (_dismissing)
            return;
        _dismissing = true;

        // Never leave a bud ringing behind a closed panel.
        if (_ringing is not null)
            StopRinging();
        Close();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Render();

    private void OnBatteryUpdated()
    {
        LoadHistory();
        Render();
    }

    private void LoadHistory() => _samples = _history.Load(DateTime.UtcNow);

    private void BuildSegments()
    {
        _ancOn = Segment("ANC", AncGrid, () => SetAnc(_state.AncLevel));
        _ancTransparency = Segment("TRANSP", AncGrid, () => SetAnc(AncMode.Transparency));
        _ancOff = Segment("OFF", AncGrid, () => SetAnc(AncMode.Off));

        foreach ((AncMode mode, string label) in new[]
        {
            (AncMode.Low, "LOW"), (AncMode.Mid, "MID"), (AncMode.High, "HIGH"), (AncMode.Adaptive, "AUTO"),
        })
        {
            _ancLevels[mode] = Segment(label, AncLevelGrid, () => SetAnc(mode));
        }

        foreach (EqPreset preset in Enum.GetValues<EqPreset>())
            _eqPresets[preset] = Segment(preset == EqPreset.Dirac ? "DIRAC" : Labels.Eq(preset).ToUpperInvariant(), EqGrid, () => _ = _session.SetEqAsync(preset));

        Segment("OFF", BassGrid, () => _ = _session.SetBassAsync(false, _state.BassLevel));
        for (int level = 1; level <= 5; level++)
        {
            int value = level;
            _bassLevels.Add(Segment(level.ToString(), BassGrid, () => _ = _session.SetBassAsync(true, value)));
        }

        _ringLeft = Segment("LEFT", FindGrid, () => OnRingClick(Bud.Left));
        _ringRight = Segment("RIGHT", FindGrid, () => OnRingClick(Bud.Right));
        _ringStop = Segment("STOP", FindGrid, StopRinging);

        _rangeDay = Segment("24 ЧАСА", RangeGrid, () => _range = DayRange);
        _rangeWeek = Segment("7 ДНЕЙ", RangeGrid, () => _range = WeekRange);
    }

    private ToggleButton Segment(string text, Panel host, Action onClick)
    {
        var button = new ToggleButton
        {
            Content = text,
            Style = (Style)FindResource("Segment"),
        };
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) =>
        {
            onClick();
            // WPF has flipped IsChecked already; the state decides what stays selected.
            Render();
        };
        host.Children.Add(button);
        return button;
    }

    private void SetAnc(AncMode mode) => _ = _session.SetAncAsync(mode);

    private void OnRingClick(Bud bud)
    {
        // Two clicks: the sound is loud enough to hurt if the bud is still in an ear.
        if (_armedRing != bud)
        {
            _armedRing = bud;
            return;
        }

        if (_ringing is { } other && other != bud)
            _ = _session.RingAsync(other, false);
        _armedRing = null;
        _ringing = bud;
        _ = _session.RingAsync(bud, true);
    }

    private void StopRinging()
    {
        _ringing = null;
        Disarm();
        _ = StopBothAsync();

        async Task StopBothAsync()
        {
            await _session.RingAsync(Bud.Left, false);
            await _session.RingAsync(Bud.Right, false);
        }
    }

    private void Disarm() => _armedRing = null;

    private void OnLatencyClick(object sender, RoutedEventArgs e) => _ = _session.SetLowLatencyAsync(LatencySwitch.IsChecked == true);

    private void OnInEarClick(object sender, RoutedEventArgs e) => _ = _session.SetInEarDetectionAsync(InEarSwitch.IsChecked == true);

    private void OnAutoStartClick(object sender, RoutedEventArgs e) => AutoStart.IsEnabled = AutoStartSwitch.IsChecked == true;

    private void OnQuitClick(object sender, RoutedEventArgs e) => _quit();

    private void Render()
    {
        bool away = _state.Link == LinkState.Disconnected;
        bool online = _state.Link == LinkState.Online;

        SetVisible(OfflineView, away);
        SetVisible(OnlineView, !away);
        Controls.IsEnabled = online;

        NameText.Text = (away || string.IsNullOrEmpty(_state.Name) ? "BudsTray" : _state.Name).ToUpperInvariant();
        StatusDot.Fill = (Brush)FindResource(online ? "Text" : "Red");
        StatusText.Text = online ? "" : away ? "НЕТ СВЯЗИ" : "ПОДКЛЮЧАЮСЬ";
        AutoStartSwitch.IsChecked = AutoStart.IsEnabled;

        RenderBud(LeftNumber, LeftCaption, "LEFT", _state.Left);
        RenderBud(RightNumber, RightCaption, "RIGHT", _state.Right);
        CaseCaption.Text = CaseText();

        BatteryEstimate estimate = BatteryForecast.Estimate(_samples, DateTime.UtcNow, _state.Left, _state.Right);
        RenderForecast(estimate);

        RenderAnc();
        RenderEq();
        RenderBass();
        RenderHistory(estimate);
        RenderProfiles();

        LatencySwitch.IsChecked = _state.LowLatency;
        InEarSwitch.IsChecked = _state.InEarDetection;

        RenderFind();
        FirmwareText.Text = string.IsNullOrEmpty(_state.Firmware) ? "" : $"FW {_state.Firmware}";
        HotkeyText.Text = _hotkey.IsRegistered ? $"{GlobalHotkey.Label} · ANC/TRANSP" : $"{GlobalHotkey.Label} ЗАНЯТО";
    }

    private void RenderForecast(BatteryEstimate estimate)
    {
        bool charging = _state.Left is { Charging: true } || _state.Right is { Charging: true };
        SetVisible(ForecastCaption, !charging && (_state.Left is not null || _state.Right is not null));
        ForecastCaption.Text = estimate.Remaining is { } remaining
            ? $"ХВАТИТ {Labels.Remaining(remaining)}"
            : "ХВАТИТ -- · прогноз появится чуть позже";
    }

    private void RenderHistory(BatteryEstimate estimate)
    {
        bool open = HistoryDisclosure.IsChecked == true;
        SetVisible(HistorySection, open);
        HistoryChevron.Text = open ? "⌄" : "›";
        if (!open)
            return;

        Chart.SetData(BatteryHistory.Columns(_samples, DateTime.UtcNow, _range, DotChart.Columns));
        _rangeDay.IsChecked = _range == DayRange;
        _rangeWeek.IsChecked = _range == WeekRange;
        RateCaption.Text = estimate.LeftPerHour is null && estimate.RightPerHour is null
            ? "РАСХОД -- · пока мало данных"
            : $"РАСХОД В ЧАС · L {Rate(estimate.LeftPerHour)} · R {Rate(estimate.RightPerHour)}";
    }

    private static string Rate(double? percentPerHour) =>
        percentPerHour is { } rate ? $"{Math.Round(rate):0} %" : "--";

    private void RenderProfiles()
    {
        bool open = ProfilesDisclosure.IsChecked == true;
        SetVisible(ProfilesSection, open);
        ProfilesChevron.Text = open ? "⌄" : "›";
        ProfilesValue.Text = _profiles.ActiveProcess?.ToUpperInvariant() ?? "";
    }

    private void RenderBud(DotMatrixNumber number, TextBlock caption, string name, BatteryCell? cell)
    {
        number.Text = cell is { } c ? c.Level.ToString() : "--";
        number.DotBrush = (Brush)FindResource(cell switch
        {
            null => "Dim",
            { Charging: false, Level: <= TrayGlyph.LowBatteryLevel } => "Red",
            _ => "Text",
        });
        caption.Text = cell is { Charging: true } ? $"{name} · ЗАРЯД" : name;
    }

    private string CaseText()
    {
        if (_state.CaseLevel is not { } level)
            return "CASE -- · открой крышку, чтобы узнать заряд";
        if (_state.CaseLive)
            return _state.CaseCharging ? $"CASE {level} · ЗАРЯД" : $"CASE {level}";
        return _state.CaseSeenAt is { } seen ? $"CASE {level} · {Labels.Age(seen, DateTime.Now)}" : $"CASE {level}";
    }

    private void RenderAnc()
    {
        AncMode? anc = _state.Anc;
        bool cancelling = anc is AncMode.Low or AncMode.Mid or AncMode.High or AncMode.Adaptive;
        _ancOn.IsChecked = cancelling;
        _ancTransparency.IsChecked = anc == AncMode.Transparency;
        _ancOff.IsChecked = anc == AncMode.Off;

        SetVisible(AncLevelGrid, cancelling);
        foreach ((AncMode mode, ToggleButton button) in _ancLevels)
            button.IsChecked = anc == mode;
    }

    private void RenderEq()
    {
        SetVisible(EqSection, _state.SupportsEq);
        EqValue.Text = Labels.Eq(_state.Eq).ToUpperInvariant();

        bool open = EqDisclosure.IsChecked == true;
        SetVisible(EqGrid, open);
        EqChevron.Text = open ? "⌄" : "›";
        foreach ((EqPreset preset, ToggleButton button) in _eqPresets)
            button.IsChecked = _state.Eq == preset;
    }

    private void RenderBass()
    {
        SetVisible(BassSection, _state.SupportsEq);
        ((ToggleButton)BassGrid.Children[0]).IsChecked = !_state.BassEnabled;
        for (int i = 0; i < _bassLevels.Count; i++)
            _bassLevels[i].IsChecked = _state.BassEnabled && _state.BassLevel == i + 1;
    }

    private void RenderFind()
    {
        bool open = FindDisclosure.IsChecked == true;
        SetVisible(FindSection, open);
        FindChevron.Text = open ? "⌄" : "›";

        _ringLeft.Content = RingLabel(Bud.Left, "LEFT");
        _ringRight.Content = RingLabel(Bud.Right, "RIGHT");
        _ringLeft.IsChecked = _ringing == Bud.Left;
        _ringRight.IsChecked = _ringing == Bud.Right;
        _ringStop.IsChecked = false;
        _ringStop.IsEnabled = _ringing is not null;
        FindHint.Text = _armedRing is null
            ? "Сигнал очень громкий. Сначала достань их из ушей."
            : "Нажми ещё раз, чтобы включить сигнал.";
    }

    private string RingLabel(Bud bud, string name) => _armedRing == bud ? "ТОЧНО?" : name;

    private static void SetVisible(UIElement element, bool visible) =>
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void PlayOpenAnimation()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, OpenDuration) { EasingFunction = ease });
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, OpenDuration) { EasingFunction = ease });
    }

    /// <summary>Puts the panel in the screen corner next to the tray, whichever edge the taskbar is on.</summary>
    private void Place()
    {
        if (!IsVisible)
            return;

        Forms.Screen screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        System.Drawing.Rectangle area = screen.WorkingArea;
        System.Drawing.Rectangle bounds = screen.Bounds;

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);

        int x = area.Left > bounds.Left ? area.Left : area.Right - width;
        int y = area.Top > bounds.Top ? area.Top : area.Bottom - height;

        IntPtr handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
