using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using BudsTray.Core;
using BudsTray.Protocol;
using Microsoft.Win32;
using LinkState = BudsTray.Core.LinkState;

namespace BudsTray.UI;

/// <summary>Shown only while the buds are connected.</summary>
public sealed class TrayIcon : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int NotificationMilliseconds = 5000;

    private readonly BudsState _state;
    private readonly AppSettings _settings;
    private readonly NotifyIcon _icon;
    private readonly Action _open;
    private readonly Action _quit;
    private Icon? _current;

    public TrayIcon(BudsState state, AppSettings settings, Action open, Action toggleTransparency, Action quit)
    {
        _state = state;
        _settings = settings;
        _open = open;
        _quit = quit;
        _icon = new NotifyIcon { ContextMenuStrip = BuildMenu() };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                open();
            else if (e.Button == MouseButtons.Middle)
                toggleTransparency();
        };

        _state.PropertyChanged += OnStateChanged;
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        Redraw();
    }

    public bool Visible
    {
        get => _icon.Visible;
        set => _icon.Visible = value;
    }

    /// <summary>Shows the texts of the language that was just selected.</summary>
    public void RefreshTexts()
    {
        ContextMenuStrip old = _icon.ContextMenuStrip!;
        _icon.ContextMenuStrip = BuildMenu();
        old.Dispose();
        Redraw();
    }

    public void Notify(string title, string text) => _icon.ShowBalloonTip(NotificationMilliseconds, title, text, ToolTipIcon.None);

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BudsState.Left):
            case nameof(BudsState.Right):
            case nameof(BudsState.CaseLevel):
            case nameof(BudsState.Anc):
            case nameof(BudsState.Name):
            case nameof(BudsState.Link):
                Redraw();
                break;
        }
    }

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        Icon next = TrayGlyph.Render(SystemInformation.SmallIconSize.Width, _state.Left?.Level, _state.Right?.Level, IsTaskbarLight());
        _icon.Icon = next;
        _current?.Dispose();
        _current = next;
        _icon.Text = BuildTooltip();
    }

    private string BuildTooltip()
    {
        var lines = new List<string> { string.IsNullOrEmpty(_state.Name) ? "BudsTray" : _state.Name };
        if (_state.Link != LinkState.Online)
        {
            lines.Add(Strings.Current.Connecting);
        }
        else
        {
            lines.Add($"L {Percent(_state.Left)} · R {Percent(_state.Right)}"
                + (_state.CaseLevel is { } level ? $" · {Strings.Current.TooltipCase} {level}%" : ""));
            if (Labels.Anc(_state.Anc) is { } anc)
                lines.Add(anc);
        }

        string text = string.Join("\n", lines);
        // NotifyIcon throws above this length.
        return text.Length <= 127 ? text : text[..127];
    }

    private static string Percent(BatteryCell? cell) => cell is { } c ? $"{c.Level}%" : "—";

    private static bool IsTaskbarLight()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            BackColor = DarkMenuRenderer.Background,
            ForeColor = Color.FromArgb(0xF4, 0xF4, 0xF4),
        };

        menu.Items.Add(Strings.Current.OpenPanel, null, (_, _) => _open());
        var autoStart = new ToolStripMenuItem(Strings.Current.StartWithWindows) { CheckOnClick = true };
        autoStart.CheckedChanged += (_, _) => AutoStart.IsEnabled = autoStart.Checked;
        menu.Items.Add(autoStart);
        var popup = new ToolStripMenuItem(Strings.Current.ConnectionPopup) { CheckOnClick = true };
        popup.Click += (_, _) =>
        {
            _settings.ConnectionPopup = popup.Checked;
            _settings.Save();
        };
        menu.Items.Add(popup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.Current.Quit, null, (_, _) => _quit());

        menu.Opening += (_, _) =>
        {
            autoStart.Checked = AutoStart.IsEnabled;
            popup.Checked = _settings.ConnectionPopup;
        };
        return menu;
    }

    public void Dispose()
    {
        _state.PropertyChanged -= OnStateChanged;
        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _current?.Dispose();
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public static readonly Color Background = Color.FromArgb(0x1C, 0x1C, 0x1C);
        private static readonly Color Hover = Color.FromArgb(0x2E, 0x2E, 0x2E);
        private static readonly Color Line = Color.FromArgb(0x33, 0x33, 0x33);

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Background);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Line);
            e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            using var brush = new SolidBrush(e.Item.Selected ? Hover : Background);
            e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Background);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Line);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 0, y, e.Item.Width, y);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            using var pen = new Pen(Color.White, 2) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int cx = e.ImageRectangle.X + e.ImageRectangle.Width / 2;
            int cy = e.ImageRectangle.Y + e.ImageRectangle.Height / 2;
            e.Graphics.DrawLines(pen, new[] { new Point(cx - 5, cy), new Point(cx - 1, cy + 4), new Point(cx + 5, cy - 4) });
        }
    }
}
