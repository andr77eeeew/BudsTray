using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BudsTray.Core;
using BudsTray.Protocol;
using Forms = System.Windows.Forms;

namespace BudsTray.UI;

/// <summary>
/// The card that appears by itself next to the tray: the buds arrived, or one of them is running low.
/// It never takes the focus from what the user is doing.
/// </summary>
public partial class BudsPopup : Window
{
    private const double GlyphRowHeight = 120;
    private const double NumberPitch = 4;

    private const double CardMilliseconds = 220;
    private const double RevealStartMilliseconds = 60;
    private const double RevealMilliseconds = 600;
    private const double FillStartMilliseconds = 340;
    private const double FillMilliseconds = 700;
    private const double ColumnDelayMilliseconds = 80;
    private const double LeaveMilliseconds = 200;
    private const double PulseMilliseconds = 700;
    private const double PulseOpacity = 0.45;

    private static readonly TimeSpan ConnectedHold = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan LowBatteryHold = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan HoverRecheck = TimeSpan.FromSeconds(1);

    private readonly List<Column> _columns = new();
    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _hold = new();
    private readonly bool _animate = SystemParameters.ClientAreaAnimation;
    private readonly bool _pulse;
    private bool _framing;
    private bool _closing;

    private BudsPopup(string name, string status, Brush statusBrush, TimeSpan hold, bool pulse, Action open)
    {
        InitializeComponent();

        NameText.Text = (string.IsNullOrEmpty(name) ? "BudsTray" : name).ToUpperInvariant();
        StatusText.Text = status;
        StatusText.Foreground = statusBrush;
        StatusDot.Fill = statusBrush;
        AutomationProperties.SetName(this, $"{name}: {status}");
        Title = status;

        _pulse = pulse;
        _hold.Interval = hold;
        _hold.Tick += (_, _) => OnHoldElapsed();

        Card.MouseLeftButtonUp += (_, _) => open();
        Card.MouseRightButtonUp += (_, _) => Dismiss();
        SizeChanged += (_, _) => Place();
        Closed += (_, _) => StopEverything();
    }

    public static BudsPopup Connected(BudsState state, Action open)
    {
        var popup = new BudsPopup(state.Name, Strings.Current.PopupConnected, Brush("Text"), ConnectedHold, pulse: false, open);
        popup.AddBud(BudsShape.LeftBud, "LEFT", state.Left);
        popup.AddBud(BudsShape.RightBud, "RIGHT", state.Right);
        popup.AddColumn(BudsShape.Case, "CASE", state.CaseLevel, Brush(state.CaseLive ? "Text" : "Dim"));
        return popup;
    }

    /// <summary>Shows only the buds the alert is about.</summary>
    public static BudsPopup LowBattery(BudsState state, LowBatteryAlert alert, Action open)
    {
        var popup = new BudsPopup(state.Name, Labels.LowBud(alert), Brush("Red"), LowBatteryHold, pulse: true, open);
        if (alert.Left is { } left)
            popup.AddColumn(BudsShape.LeftBud, "LEFT", left, Brush("Red"));
        if (alert.Right is { } right)
            popup.AddColumn(BudsShape.RightBud, "RIGHT", right, Brush("Red"));
        return popup;
    }

    public void Open()
    {
        Show();
        Place();
        if (!_animate)
        {
            ShowFinalState();
            _hold.Start();
            return;
        }

        ShowFrame(0);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(CardMilliseconds);
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, duration) { EasingFunction = ease });
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

        _framing = true;
        CompositionTarget.Rendering += OnFrame;
    }

    /// <summary>Closes at once, for when something else takes over the corner.</summary>
    public void Dismiss()
    {
        if (_closing)
            return;
        _closing = true;
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr handle = new WindowInteropHelper(this).Handle;
        SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExNoActivate | WsExToolWindow);
    }

    private void AddBud(BudsShape shape, string caption, BatteryCell? cell) =>
        AddColumn(shape, caption, cell?.Level, Brush(cell switch
        {
            null => "Dim",
            { Charging: false, Level: <= TrayGlyph.LowBatteryLevel } => "Red",
            _ => "Text",
        }));

    private void AddColumn(BudsShape shape, string caption, int? level, Brush brush)
    {
        var glyph = new BudsGlyph
        {
            Shape = shape,
            DotBrush = brush,
            EmptyBrush = Brush("DotEmpty"),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var number = new DotMatrixNumber
        {
            Pitch = NumberPitch,
            DotBrush = brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var label = new TextBlock
        {
            Text = caption,
            Style = (Style)FindResource("Caption"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var column = new StackPanel();
        column.Children.Add(new Grid { Height = GlyphRowHeight, Children = { glyph } });
        column.Children.Add(number);
        column.Children.Add(label);
        Columns.Children.Add(column);

        _columns.Add(new Column(glyph, number, level));
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        // The first show of a window can take a moment; the animation must not run out behind it.
        _clock.Start();
        double elapsed = _clock.Elapsed.TotalMilliseconds;
        ShowFrame(elapsed);

        double end = FillStartMilliseconds + FillMilliseconds + ColumnDelayMilliseconds * (_columns.Count - 1);
        if (elapsed < end)
            return;

        StopFraming();
        ShowFinalState();
        _hold.Start();
    }

    private void ShowFrame(double elapsed)
    {
        for (int i = 0; i < _columns.Count; i++)
        {
            double delay = i * ColumnDelayMilliseconds;
            double reveal = Progress(elapsed - RevealStartMilliseconds - delay, RevealMilliseconds);
            double fill = EaseOut(Progress(elapsed - FillStartMilliseconds - delay, FillMilliseconds));
            ShowColumn(_columns[i], reveal, fill);
        }
    }

    private void ShowFinalState()
    {
        foreach (Column column in _columns)
        {
            ShowColumn(column, 1, 1);
            if (_pulse && _animate)
            {
                column.Glyph.BeginAnimation(OpacityProperty, new DoubleAnimation(1, PulseOpacity, TimeSpan.FromMilliseconds(PulseMilliseconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
            }
        }
        if (!_animate)
            Card.Opacity = 1;
    }

    private static void ShowColumn(Column column, double reveal, double fill)
    {
        column.Glyph.Reveal = reveal;
        column.Glyph.Fill = (column.Level ?? 0) / 100.0 * fill;
        column.Number.Text = column.Level is { } level ? ((int)Math.Round(level * fill)).ToString() : "--";
    }

    private void OnHoldElapsed()
    {
        // Someone reading the card, or about to click it, keeps it there.
        if (IsMouseOver)
        {
            _hold.Interval = HoverRecheck;
            return;
        }
        _hold.Stop();
        Leave();
    }

    private void Leave()
    {
        if (_closing)
            return;
        if (!_animate)
        {
            Dismiss();
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var duration = TimeSpan.FromMilliseconds(LeaveMilliseconds);
        var fade = new DoubleAnimation(0, duration) { EasingFunction = ease };
        fade.Completed += (_, _) => Dismiss();
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, duration) { EasingFunction = ease });
        Card.BeginAnimation(OpacityProperty, fade);
    }

    private void StopFraming()
    {
        if (!_framing)
            return;
        _framing = false;
        CompositionTarget.Rendering -= OnFrame;
        _clock.Stop();
    }

    private void StopEverything()
    {
        _closing = true;
        StopFraming();
        _hold.Stop();
        foreach (Column column in _columns)
            column.Glyph.BeginAnimation(OpacityProperty, null);
    }

    // The taskbar is on the main screen, whichever screen the cursor is on.
    private void Place() => TrayCorner.Place(this, Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]);

    private static double Progress(double elapsed, double duration) => Math.Clamp(elapsed / duration, 0, 1);

    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    private sealed record Column(BudsGlyph Glyph, DotMatrixNumber Number, int? Level);

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
}
