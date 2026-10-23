#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioLeash;

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
}
