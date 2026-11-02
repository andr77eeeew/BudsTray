using System;

namespace BudsTray.Core;

/// <summary>
/// Decides when connecting deserves the pop-up: only when the buds really arrived, and only once
/// their charge is known, since the charge is what the pop-up is there to show.
/// </summary>
public sealed class ConnectionPopupPolicy
{
    // Buds that were already connected when the app started did not just arrive.
    private static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(10);

    // The link drops for a moment now and then, which is not the user taking the buds out.
    private static readonly TimeSpan BriefDropout = TimeSpan.FromSeconds(30);

    // A report this late would announce something the user has long noticed.
    private static readonly TimeSpan WaitForBattery = TimeSpan.FromSeconds(30);

    private readonly DateTime _startedAt;
    private DateTime? _disconnectedAt;
    private DateTime? _waitingUntil;

    public ConnectionPopupPolicy(DateTime startedAt) => _startedAt = startedAt;

    public void OnConnected(DateTime now)
    {
        bool atStartup = now - _startedAt < StartupGrace;
        bool dropout = _disconnectedAt is { } at && now - at < BriefDropout;
        _waitingUntil = atStartup || dropout ? null : now + WaitForBattery;
    }

    public void OnDisconnected(DateTime now)
    {
        _disconnectedAt = now;
        _waitingUntil = null;
    }

    /// <summary>A battery report arrived. True means the pop-up should show now; it says so once per connection.</summary>
    public bool OnBattery(DateTime now, bool chargeKnown)
    {
        if (_waitingUntil is not { } until)
            return false;
        if (now > until)
        {
            _waitingUntil = null;
            return false;
        }
        if (!chargeKnown)
            return false;

        _waitingUntil = null;
        return true;
    }
}
