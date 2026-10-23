#nullable enable
using System;
using System.Collections.Generic;

namespace AudioLeash;

/// <summary>
/// One flow's persisted state: the single pick (null when none) and its ranked priority list.
/// </summary>
public sealed record FlowSnapshot(RankedDevice? Pick, IReadOnlyList<RankedDevice> Priority)
{
    public static FlowSnapshot Empty { get; } = new(null, Array.Empty<RankedDevice>());

    /// <summary>
    /// Builds a flow from live state. With a ranked list (priority mode) the pick is the manual
    /// override; without one (legacy mode) it is the selected device.
    /// </summary>
    public static FlowSnapshot From(
        IReadOnlyList<RankedDevice> ranked, RankedDevice? overrideDevice, RankedDevice? selectedDevice)
        => new(ranked.Count > 0 ? overrideDevice : selectedDevice, ranked);
}

/// <summary>Everything AudioLeash persists, as one immutable value.</summary>
public sealed record SettingsSnapshot(
    FlowSnapshot Playback,
    FlowSnapshot Recording,
    IReadOnlyList<RankedDevice> KeepAlive)
{
    public static SettingsSnapshot Empty { get; } =
        new(FlowSnapshot.Empty, FlowSnapshot.Empty, Array.Empty<RankedDevice>());
}
