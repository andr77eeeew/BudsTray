using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using BudsTray.Bluetooth;
using BudsTray.Core;
using BudsTray.Protocol;

namespace BudsTray.UI;

public partial class ProfilesPanel : UserControl
{
    private const int MaxNameLength = 20;

    private BudsState _state = null!;
    private ProfileRunner _runner = null!;

    private bool _editorOpen;
    private string? _editing;
    private AncMode? _anc;
    private bool? _latency;

    private ToggleButton _ancNone = null!, _ancOn = null!, _ancTransparency = null!, _ancOff = null!;
    private ToggleButton _latencyNone = null!, _latencyOn = null!, _latencyOff = null!;
    private ToggleButton _save = null!, _remove = null!;

    public ProfilesPanel() => InitializeComponent();

    public void Attach(BudsState state, ProfileRunner runner)
    {
        _state = state;
        _runner = runner;
        BuildEditor();
        _runner.Changed += Render;
        Render();
    }

    public void Detach() => _runner.Changed -= Render;

    private void BuildEditor()
    {
        _ancNone = Segment("—", AncChoice, () => _anc = null);
        _ancOn = Segment("ANC", AncChoice, () => _anc = IsCancelling(_anc) ? _anc : _state.AncLevel);
        _ancTransparency = Segment("TRANSP", AncChoice, () => _anc = AncMode.Transparency);
        _ancOff = Segment("OFF", AncChoice, () => _anc = AncMode.Off);

        _latencyNone = Segment("—", LatencyChoice, () => _latency = null);
        _latencyOn = Segment("ВКЛ", LatencyChoice, () => _latency = true);
        _latencyOff = Segment("ВЫКЛ", LatencyChoice, () => _latency = false);

        _save = Segment("СОХРАНИТЬ", EditorButtons, Save);
        _remove = Segment("УДАЛИТЬ", EditorButtons, Remove);
        Segment("ОТМЕНА", EditorButtons, CloseEditor);
    }

    private ToggleButton Segment(string text, Panel host, Action onClick)
    {
        var button = new ToggleButton { Content = text, Style = (Style)FindResource("Segment") };
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) =>
        {
            onClick();
            Render();
        };
        host.Children.Add(button);
        return button;
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => OpenEditor(null);

    private void OnProcessChanged(object sender, TextChangedEventArgs e)
    {
        if (_editorOpen)
            RenderEditor();
    }

    private void OpenEditor(AppProfile? profile)
    {
        _editorOpen = true;
        _editing = profile?.Process;
        _anc = profile?.Anc;
        _latency = profile?.LowLatency;
        ProcessBox.Text = profile?.Process ?? "";
        _ = LoadRunningAsync();
        Render();
    }

    private void CloseEditor() => _editorOpen = false;

    private void Save()
    {
        string name = RunningApps.NormalizeName(ProcessBox.Text);
        List<AppProfile> kept = _runner.Profiles
            .Where(p => !IsNamed(p, _editing) && !IsNamed(p, name))
            .ToList();
        kept.Add(new AppProfile(name, _anc, _latency));
        CloseEditor();
        _runner.Save(kept);
    }

    private void Remove()
    {
        List<AppProfile> kept = _runner.Profiles.Where(p => !IsNamed(p, _editing)).ToList();
        CloseEditor();
        _runner.Save(kept);
    }

    private static bool IsNamed(AppProfile profile, string? name) =>
        string.Equals(profile.Process, name, StringComparison.OrdinalIgnoreCase);

    private async Task LoadRunningAsync()
    {
        RunningGrid.Children.Clear();
        List<string> names = await Task.Run(RunningApps.Windowed);
        if (!_editorOpen)
            return;

        foreach (string name in names)
        {
            string value = name;
            ToggleButton button = Segment(Shorten(name), RunningGrid, () => ProcessBox.Text = value);
            button.Tag = value;
        }
        Render();
    }

    private static string Shorten(string name) =>
        name.Length <= MaxNameLength ? name : name[..(MaxNameLength - 1)] + "…";

    private void Render()
    {
        ListView.Visibility = _editorOpen ? Visibility.Collapsed : Visibility.Visible;
        Editor.Visibility = _editorOpen ? Visibility.Visible : Visibility.Collapsed;

        if (_editorOpen)
            RenderEditor();
        else
            RenderRows();
    }

    private void RenderRows()
    {
        Rows.Children.Clear();
        foreach (AppProfile profile in _runner.Profiles)
            Rows.Children.Add(BuildRow(profile));
    }

    private ToggleButton BuildRow(AppProfile profile)
    {
        var row = new DockPanel();

        var summary = new TextBlock
        {
            Text = Labels.Profile(profile),
            Style = (Style)FindResource("Caption"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(summary, Dock.Right);
        row.Children.Add(summary);

        if (IsNamed(profile, _runner.ActiveProcess))
        {
            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = (Brush)FindResource("Text"),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
        }

        row.Children.Add(new TextBlock { Text = Shorten(profile.Process), Style = (Style)FindResource("RowLabel") });

        var button = new ToggleButton { Content = row, Style = (Style)FindResource("Disclosure") };
        AutomationProperties.SetName(button, profile.Process);
        button.Click += (_, _) => OpenEditor(profile);
        return button;
    }

    private void RenderEditor()
    {
        _ancNone.IsChecked = _anc is null;
        _ancOn.IsChecked = IsCancelling(_anc);
        _ancTransparency.IsChecked = _anc == AncMode.Transparency;
        _ancOff.IsChecked = _anc == AncMode.Off;

        _latencyNone.IsChecked = _latency is null;
        _latencyOn.IsChecked = _latency == true;
        _latencyOff.IsChecked = _latency == false;

        string name = RunningApps.NormalizeName(ProcessBox.Text);
        foreach (ToggleButton app in RunningGrid.Children.OfType<ToggleButton>())
            app.IsChecked = string.Equals(app.Tag as string, name, StringComparison.OrdinalIgnoreCase);

        _save.IsChecked = false;
        _save.IsEnabled = name.Length > 0 && (_anc is not null || _latency is not null);
        _remove.IsChecked = false;
        _remove.Visibility = _editing is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool IsCancelling(AncMode? mode) =>
        mode is AncMode.Low or AncMode.Mid or AncMode.High or AncMode.Adaptive;
}
