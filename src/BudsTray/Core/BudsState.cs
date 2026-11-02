using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BudsTray.Protocol;

namespace BudsTray.Core;

public enum LinkState
{
    /// <summary>The buds are not connected to this PC.</summary>
    Disconnected,
    /// <summary>The buds are connected, the control channel is not open yet.</summary>
    Connecting,
    Online,
}

/// <summary>What is known about the buds. Written and read on one thread only (the UI thread in the app).</summary>
public sealed class BudsState : INotifyPropertyChanged
{
    private const string ModelWithKnownEq = "CMF Buds Pro 2";

    private LinkState _link;
    private string _name = "";
    private string _firmware = "";
    private BatteryCell? _left, _right;
    private int? _caseLevel;
    private bool _caseCharging, _caseLive;
    private DateTime? _caseSeenAt;
    private AncMode? _anc;
    private AncMode _ancLevel = AncMode.High;
    private EqPreset? _eq;
    private bool _bassEnabled;
    private int _bassLevel = 1;
    private bool _lowLatency;
    private bool _inEarDetection;

    public BudsState()
    {
        if (CaseCache.Load() is { } cached)
        {
            _caseLevel = cached.Level;
            _caseSeenAt = cached.SeenAt;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>A battery report arrived, even if it changed nothing: both buds are up to date at this point.</summary>
    public event Action? BatteryUpdated;

    public LinkState Link { get => _link; set => Set(ref _link, value); }
    public string Name { get => _name; set { if (Set(ref _name, value)) Notify(nameof(SupportsEq)); } }
    public string Firmware { get => _firmware; set => Set(ref _firmware, value); }
    public BatteryCell? Left { get => _left; set => Set(ref _left, value); }
    public BatteryCell? Right { get => _right; set => Set(ref _right, value); }
    public int? CaseLevel { get => _caseLevel; private set => Set(ref _caseLevel, value); }
    public bool CaseCharging { get => _caseCharging; private set => Set(ref _caseCharging, value); }

    /// <summary>True when the reading came from the latest report rather than from memory.</summary>
    public bool CaseLive { get => _caseLive; private set => Set(ref _caseLive, value); }
    public DateTime? CaseSeenAt { get => _caseSeenAt; private set => Set(ref _caseSeenAt, value); }
    public AncMode? Anc { get => _anc; set => Set(ref _anc, value); }

    /// <summary>The strength to use when noise cancelling is switched on again.</summary>
    public AncMode AncLevel { get => _ancLevel; set => Set(ref _ancLevel, value); }
    public EqPreset? Eq { get => _eq; set => Set(ref _eq, value); }
    public bool BassEnabled { get => _bassEnabled; set => Set(ref _bassEnabled, value); }
    public int BassLevel { get => _bassLevel; set => Set(ref _bassLevel, value); }
    public bool LowLatency { get => _lowLatency; set => Set(ref _lowLatency, value); }
    public bool InEarDetection { get => _inEarDetection; set => Set(ref _inEarDetection, value); }

    /// <summary>EQ ids and the bass encoding differ per model; only offer them where they were verified.</summary>
    public bool SupportsEq => Name.Contains(ModelWithKnownEq, StringComparison.OrdinalIgnoreCase);

    public void ApplyBattery(BatteryReport report)
    {
        Left = report.Left;
        Right = report.Right;

        if (report.Case is { } cell)
        {
            CaseLevel = cell.Level;
            CaseCharging = cell.Charging;
            CaseLive = true;
            CaseSeenAt = DateTime.Now;
            CaseCache.Save(new CaseCache.Entry(cell.Level, DateTime.Now));
        }
        else
        {
            MarkCaseStale();
        }

        BatteryUpdated?.Invoke();
    }

    public void MarkOffline()
    {
        Left = null;
        Right = null;
        Anc = null;
        Eq = null;
        MarkCaseStale();
    }

    // A remembered reading must not keep claiming the case is charging.
    private void MarkCaseStale()
    {
        CaseLive = false;
        CaseCharging = false;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        Notify(property);
        return true;
    }

    private void Notify(string? property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
