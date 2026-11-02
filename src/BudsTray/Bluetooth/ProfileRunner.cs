using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BudsTray.Core;

namespace BudsTray.Bluetooth;

/// <summary>Applies application profiles while the buds are connected. Without profiles it does no work.</summary>
public sealed class ProfileRunner
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    private readonly BudsState _state;
    private readonly BudsSession _session;
    private readonly ProfileResolver _resolver = new();
    private CancellationTokenSource? _lifetime;

    public ProfileRunner(BudsState state, BudsSession session)
    {
        _state = state;
        _session = session;
        Profiles = ProfileStore.Load();
    }

    public event Action? Changed;

    public IReadOnlyList<AppProfile> Profiles { get; private set; }

    public string? ActiveProcess => _resolver.Active?.Process;

    public void Start()
    {
        Stop();
        _lifetime = new CancellationTokenSource();
        _ = RunAsync(_lifetime.Token);
    }

    public void Stop()
    {
        _lifetime?.Cancel();
        _lifetime = null;
    }

    public void Save(IReadOnlyList<AppProfile> profiles)
    {
        Profiles = profiles;
        ProfileStore.Save(profiles);
        Changed?.Invoke();
        _ = CheckAsync();
    }

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
                await CheckAsync();
        }
        catch (OperationCanceledException)
        {
            // The buds went away.
        }
    }

    private async Task CheckAsync()
    {
        if (!_session.SettingsKnown || (Profiles.Count == 0 && _resolver.Active is null))
            return;

        HashSet<string> running = await Task.Run(RunningApps.AllNames);

        // The buds may have left during the wait.
        if (!_session.SettingsKnown || _state.Anc is not { } anc)
            return;

        string? before = ActiveProcess;
        ProfileChange? change = _resolver.Update(Profiles, running, anc, _state.LowLatency);
        if (change?.Anc is { } mode)
            _ = _session.SetAncAsync(mode);
        if (change?.LowLatency is { } latency)
            _ = _session.SetLowLatencyAsync(latency);

        if (ActiveProcess != before)
        {
            Log.Info($"Profile: {before ?? "none"} > {ActiveProcess ?? "none"}");
            Changed?.Invoke();
        }
    }
}
