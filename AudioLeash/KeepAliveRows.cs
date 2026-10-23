#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioLeash;

/// <summary>One device on the settings window's "Keep devices awake" page.</summary>
internal sealed record KeepAliveRow(DeviceRow Device, bool Enabled);

/// <summary>Pure logic behind the "Keep devices awake" page.</summary>
internal static class KeepAliveRows
{
    /// <summary>
    /// Every playback device Windows knows about, plus enabled devices it no longer lists
    /// (shown as not connected, under their saved name), sorted by name.
    /// </summary>
    public static IReadOnlyList<KeepAliveRow> Build(IEnumerable<DeviceRow> known, IReadOnlyList<RankedDevice> enabled)
    {
        var enabledIds = new HashSet<string>(enabled.Select(d => d.Id), StringComparer.Ordinal);
        var rows = new Dictionary<string, DeviceRow>(StringComparer.Ordinal);
        foreach (var row in known)
            rows.TryAdd(row.Id, row); // first one wins
        foreach (var d in enabled)
            rows.TryAdd(d.Id, new DeviceRow(d.Id, d.Name, d.Description, IsConnected: false));

        return rows.Values
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new KeepAliveRow(r, enabledIds.Contains(r.Id)))
            .ToArray();
    }

    /// <summary>The settings card's subtitle for <paramref name="enabledCount"/> enabled devices.</summary>
    public static string Summary(int enabledCount) => enabledCount switch
    {
        0 => "Off",
        1 => "On for 1 device",
        _ => $"On for {enabledCount} devices",
    };
}
