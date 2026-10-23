#nullable enable
using System;
using System.Collections.Generic;

namespace AudioLeash;

/// <summary>What to do with the keep-alive stream: stop the one running, and/or start one on a device.</summary>
internal readonly record struct KeepAlivePlan(bool StopCurrent, string? StartId);

/// <summary>
/// Pure decision logic for keep-alive: which playback device, if any, should receive the
/// silent stream. The stream follows the Windows default device and runs only while that
/// device has keep-alive enabled and nothing pauses it (locked session, display off, suspend).
/// Holds no audio objects, so every rule is unit-testable.
/// </summary>
internal sealed class KeepAliveState
{
    private List<RankedDevice> _devices = [];

    /// <param name="devices">The enabled devices; empty and duplicate IDs are ignored (first wins).</param>
    public KeepAliveState(IReadOnlyList<RankedDevice> devices) => ReplaceAll(devices);

    /// <summary>Replaces the enabled devices, keeping their order; empty and duplicate IDs are ignored (first wins).</summary>
    public void ReplaceAll(IReadOnlyList<RankedDevice> devices)
    {
        // Built aside and swapped in, so a reader never sees a half-filled list.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<RankedDevice>();
        foreach (var d in devices)
        {
            if (!string.IsNullOrEmpty(d.Id) && seen.Add(d.Id))
                list.Add(d);
        }
        _devices = list;
    }

    /// <summary>The devices with keep-alive enabled, in the order they were enabled.</summary>
    public IReadOnlyList<RankedDevice> Devices => _devices.ToArray();

    /// <summary>The current Multimedia default playback device (always active), or null if there is none.</summary>
    public string? DefaultId { get; set; }

    public bool IsLocked { get; set; }
    public bool IsDisplayOff { get; set; }
    public bool IsSuspended { get; set; }

    public bool IsEnabled(string id) => _devices.Exists(d => string.Equals(d.Id, id, StringComparison.Ordinal));

    /// <summary>Enables or disables keep-alive for a device. Returns whether anything changed.</summary>
    public bool SetEnabled(RankedDevice device, bool enabled)
    {
        if (string.IsNullOrEmpty(device.Id) || IsEnabled(device.Id) == enabled) return false;
        if (enabled) _devices.Add(device);
        else _devices.RemoveAll(d => string.Equals(d.Id, device.Id, StringComparison.Ordinal));
        return true;
    }

    /// <summary>The device that should be streaming right now, or null for none.</summary>
    public string? DesiredId =>
        DefaultId is { } id && IsEnabled(id) && !IsLocked && !IsDisplayOff && !IsSuspended ? id : null;

    /// <summary>What to do given the device currently streaming (<paramref name="runningId"/>, or null).</summary>
    public KeepAlivePlan Plan(string? runningId)
    {
        string? desired = DesiredId;
        return string.Equals(runningId, desired, StringComparison.Ordinal)
            ? new KeepAlivePlan(false, null)
            : new KeepAlivePlan(runningId is not null, desired);
    }
}
