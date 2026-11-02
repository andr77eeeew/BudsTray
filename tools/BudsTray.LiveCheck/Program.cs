// Exercises the real session against real buds. Reads by default.
// With --write it also changes each setting, checks the buds report the new value, and restores it.
// It never rings the buds: that is loud, and the buds may be in someone's ears.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using BudsTray.Bluetooth;
using BudsTray.Core;
using BudsTray.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

bool write = args.Contains("--write");

BluetoothDevice? device = null;
foreach (DeviceInformation info in await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)))
{
    BluetoothDevice candidate = await BluetoothDevice.FromIdAsync(info.Id);
    var services = await candidate.GetRfcommServicesForIdAsync(
        Windows.Devices.Bluetooth.Rfcomm.RfcommServiceId.FromUuid(BudsSession.ControlService), BluetoothCacheMode.Cached);
    if (services.Services.Count > 0)
    {
        device = candidate;
        break;
    }
}

if (device is null)
{
    Console.WriteLine("FAIL: no paired device exposes the control service");
    return 1;
}
Console.WriteLine($"Device: {device.Name}, status {device.ConnectionStatus}");

var state = new BudsState();
var session = new BudsSession(state);
session.Start(device);

for (int i = 0; i < 40 && state.Link != LinkState.Online; i++)
    await Task.Delay(250);
if (state.Link != LinkState.Online)
{
    Console.WriteLine($"FAIL: channel not online, link is {state.Link}");
    return 1;
}

await Task.Delay(500);
await session.RefreshAllAsync();
Print();

int failures = 0;
if (write)
{
    AncMode anc = state.Anc ?? AncMode.Off;
    AncMode ancLevel = state.AncLevel;
    EqPreset eq = state.Eq ?? EqPreset.Dirac;
    (bool enabled, int level) bass = (state.BassEnabled, state.BassLevel);
    bool latency = state.LowLatency;
    bool inEar = state.InEarDetection;

    await Check("ANC -> Off", () => session.SetAncAsync(AncMode.Off), () => state.Anc == AncMode.Off);
    await Check("ANC -> High", () => session.SetAncAsync(AncMode.High), () => state.Anc == AncMode.High);
    await Check("ANC -> Mid", () => session.SetAncAsync(AncMode.Mid), () => state.Anc == AncMode.Mid);
    await Check("ANC -> Low", () => session.SetAncAsync(AncMode.Low), () => state.Anc == AncMode.Low);
    await Check("ANC -> Adaptive", () => session.SetAncAsync(AncMode.Adaptive), () => state.Anc == AncMode.Adaptive);
    await Check("ANC -> Transparency", () => session.SetAncAsync(AncMode.Transparency), () => state.Anc == AncMode.Transparency);

    // Rapid clicks: the last request has to win, and the panel must not flicker through the others.
    var seen = new List<AncMode?>();
    PropertyChangedEventHandler watch = (_, e) =>
    {
        if (e.PropertyName == nameof(BudsState.Anc))
            seen.Add(state.Anc);
    };
    state.PropertyChanged += watch;
    Task first = session.SetAncAsync(AncMode.Off);
    await Task.Delay(200);
    Task second = session.SetAncAsync(AncMode.Low);
    await Task.Delay(200);
    await Check("ANC rapid clicks: last wins", () => session.SetAncAsync(AncMode.Mid), () => state.Anc == AncMode.Mid);
    await Task.WhenAll(first, second);
    state.PropertyChanged -= watch;
    Console.WriteLine($"     states shown: {string.Join(" > ", seen)}");
    if (!seen.SequenceEqual(new AncMode?[] { AncMode.Off, AncMode.Low, AncMode.Mid }))
    {
        failures++;
        Console.WriteLine("FAIL panel showed unexpected states");
    }

    await Check("ANC restore level", () => session.SetAncAsync(ancLevel), () => state.Anc == ancLevel && state.AncLevel == ancLevel);
    await Check("ANC restore mode", () => session.SetAncAsync(anc), () => state.Anc == anc);

    await Check("EQ -> Pop", () => session.SetEqAsync(EqPreset.Pop), () => state.Eq == EqPreset.Pop);
    await Check("EQ restore", () => session.SetEqAsync(eq), () => state.Eq == eq);

    await Check("Bass -> on 3", () => session.SetBassAsync(true, 3), () => state.BassEnabled && state.BassLevel == 3);
    await Check("Bass -> off", () => session.SetBassAsync(false, 3), () => !state.BassEnabled);
    await Check("Bass restore", () => session.SetBassAsync(bass.enabled, bass.level), () => state.BassEnabled == bass.enabled && state.BassLevel == bass.level);

    await Check("Latency flip", () => session.SetLowLatencyAsync(!latency), () => state.LowLatency == !latency);
    await Check("Latency restore", () => session.SetLowLatencyAsync(latency), () => state.LowLatency == latency);

    await Check("In-ear flip", () => session.SetInEarDetectionAsync(!inEar), () => state.InEarDetection == !inEar);
    await Check("In-ear restore", () => session.SetInEarDetectionAsync(inEar), () => state.InEarDetection == inEar);

    Print();
}

session.Stop();
device.Dispose();
Console.WriteLine(failures == 0 ? "OK" : $"{failures} check(s) failed");
return failures == 0 ? 0 : 1;

async Task Check(string name, Func<Task> act, Func<bool> holds)
{
    await act();
    bool ok = holds();
    if (!ok)
        failures++;
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}");
}

void Print() => Console.WriteLine(
    $"L={state.Left} R={state.Right} case={state.CaseLevel} live={state.CaseLive} anc={state.Anc} eq={state.Eq} " +
    $"bass={state.BassEnabled}/{state.BassLevel} latency={state.LowLatency} inEar={state.InEarDetection} fw={state.Firmware}");
