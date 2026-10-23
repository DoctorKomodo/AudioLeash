#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioLeash;

/// <summary>The endpoints Windows currently knows for one flow, as <see cref="DeviceRebinder.Rebind"/> takes them.</summary>
internal sealed record KnownDevices(IReadOnlySet<string> ExistingIds, IReadOnlyList<DeviceRow> Candidates);

/// <summary>
/// Re-binds saved devices whose endpoint ID Windows no longer knows to the endpoint that
/// replaced them. Reinstalling or updating a driver (an NVIDIA GPU driver reinstalls HDMI
/// audio) can give the same physical device a brand-new endpoint ID, which would otherwise
/// leave a ranked or keep-alive entry stuck as "not connected" forever.
/// Pure logic — no audio-stack dependencies — so every rule is unit-testable.
/// </summary>
internal static class DeviceRebinder
{
    /// <summary>
    /// Returns <paramref name="saved"/> with each entry whose ID is absent from
    /// <paramref name="existingIds"/> replaced by an unclaimed candidate with the same name
    /// (ignoring case) and description. An entry with no recorded description (a saved pick)
    /// matches on name alone: Windows friendly names already embed the adapter, e.g.
    /// "LG TV (NVIDIA High Definition Audio)". A single connected match wins; failing that, a
    /// single match of any state. Anything ambiguous is left alone. Order is preserved.
    /// </summary>
    /// <param name="existingIds">
    ///   Every endpoint ID Windows has for the flow in any state, NotPresent included: a USB
    ///   device that is unplugged goes NotPresent but still exists, so it is never re-bound.
    /// </param>
    /// <param name="candidates">The endpoints an entry may move to (active, unplugged or disabled).</param>
    public static IReadOnlyList<RankedDevice> Rebind(
        IReadOnlyList<RankedDevice> saved, IReadOnlySet<string> existingIds,
        IReadOnlyList<DeviceRow> candidates, out bool changed)
    {
        changed = false;
        var claimed = new HashSet<string>(saved.Select(d => d.Id), StringComparer.Ordinal);
        var result  = new List<RankedDevice>(saved.Count);

        foreach (var d in saved)
        {
            if (existingIds.Contains(d.Id))
            {
                result.Add(d);
                continue;
            }

            var matches = candidates
                .Where(r => !claimed.Contains(r.Id)
                    && string.Equals(r.Name, d.Name, StringComparison.OrdinalIgnoreCase)
                    && (d.Description is null
                        || string.Equals(r.Description, d.Description, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var connected = matches.Where(r => r.IsConnected).ToList();
            var match = connected.Count == 1 ? connected[0]
                      : connected.Count == 0 && matches.Count == 1 ? matches[0]
                      : null;

            if (match is null)
            {
                result.Add(d);
                continue;
            }

            claimed.Add(match.Id);
            result.Add(new RankedDevice(match.Id, match.Name, match.Description));
            changed = true;
        }
        return result;
    }

    /// <summary>
    /// Returns <paramref name="pick"/> carrying the current name and description of the candidate
    /// with the same ID, so a pick saved without a description gains one. Unchanged (the same
    /// instance) when no candidate matches; null stays null.
    /// </summary>
    public static RankedDevice? RefreshPick(RankedDevice? pick, IReadOnlyList<DeviceRow> candidates)
    {
        if (pick is null)
            return null;

        var row = candidates.FirstOrDefault(r => string.Equals(r.Id, pick.Id, StringComparison.Ordinal));
        return row is null ? pick : new RankedDevice(row.Id, row.Name, row.Description);
    }

    /// <summary>
    /// Re-binds and refreshes every device in <paramref name="snapshot"/> against what Windows
    /// currently knows. Keep-alive is matched against <paramref name="playback"/>. A null
    /// <see cref="KnownDevices"/> leaves that flow (and, for playback, the keep-alive list) untouched.
    /// <paramref name="changed"/> is true when any entry's ID, name or description changed.
    /// </summary>
    public static SettingsSnapshot Refresh(
        SettingsSnapshot snapshot, KnownDevices? playback, KnownDevices? recording, out bool changed)
    {
        var playbackFlow = RefreshFlow(snapshot.Playback, playback, out bool playbackChanged);
        var recordingFlow = RefreshFlow(snapshot.Recording, recording, out bool recordingChanged);

        var keepAlive = snapshot.KeepAlive;
        bool keepAliveChanged = false;
        if (playback is not null)
            keepAlive = Rebind(keepAlive, playback.ExistingIds, playback.Candidates, out keepAliveChanged);

        changed = playbackChanged || recordingChanged || keepAliveChanged;
        return snapshot with { Playback = playbackFlow, Recording = recordingFlow, KeepAlive = keepAlive };
    }

    private static FlowSnapshot RefreshFlow(FlowSnapshot flow, KnownDevices? known, out bool changed)
    {
        if (known is null)
        {
            changed = false;
            return flow;
        }

        var priority = Rebind(flow.Priority, known.ExistingIds, known.Candidates, out changed);

        var pick = flow.Pick;
        if (pick is not null)
            pick = Rebind([pick], known.ExistingIds, known.Candidates, out _)[0];
        pick = RefreshPick(pick, known.Candidates);

        changed |= !Equals(pick, flow.Pick);
        return flow with { Pick = pick, Priority = priority };
    }
}
