#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AudioLeash;

/// <summary>
/// Converts between <see cref="SettingsSnapshot"/> and the settings.json text.
/// Reads the current (version 2) shape and upgrades the unversioned v2.1.x (v1) shape.
/// </summary>
internal static class SettingsFile
{
    public const int CurrentVersion = 2;

    private const string UnknownName = "Unknown device";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Parses settings text into a sanitized snapshot. Returns null for invalid JSON, a
    /// non-object root, or a version newer than this build understands.
    /// </summary>
    public static SettingsSnapshot? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var version = 1;
            if (root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number)
            {
                if (!v.TryGetInt32(out version)) return null;
                if (version > CurrentVersion) return null;
            }

            return version == CurrentVersion
                ? FromV2(root.Deserialize<FileV2>(JsonOptions))
                : FromV1(root.Deserialize<FileV1>(JsonOptions));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the snapshot in the current (version 2) shape.</summary>
    public static string Serialize(SettingsSnapshot snapshot)
        => JsonSerializer.Serialize(new FileV2(
            CurrentVersion,
            ToFlowDto(snapshot.Playback),
            ToFlowDto(snapshot.Recording),
            snapshot.KeepAlive), JsonOptions);

    private static FlowV2 ToFlowDto(FlowSnapshot flow) => new(flow.Pick, flow.Priority);

    private static SettingsSnapshot FromV2(FileV2? file)
        => file is null
            ? SettingsSnapshot.Empty
            : new SettingsSnapshot(FromFlowDto(file.Playback), FromFlowDto(file.Recording), Sanitize(file.KeepAlive));

    private static FlowSnapshot FromFlowDto(FlowV2? flow)
        => new(SanitizePick(flow?.Pick), Sanitize(flow?.Priority));

    private static SettingsSnapshot FromV1(FileV1? file)
    {
        if (file is null) return SettingsSnapshot.Empty;
        var playbackId = file.SelectedPlaybackDeviceId ?? file.SelectedDeviceId;
        return new SettingsSnapshot(
            new FlowSnapshot(
                SanitizePick(playbackId is null ? null : new RankedDevice(playbackId, file.SelectedPlaybackDeviceName!)),
                Sanitize(file.PlaybackPriority)),
            new FlowSnapshot(
                SanitizePick(file.SelectedCaptureDeviceId is null
                    ? null
                    : new RankedDevice(file.SelectedCaptureDeviceId, file.SelectedCaptureDeviceName!)),
                Sanitize(file.CapturePriority)),
            Sanitize(file.KeepAliveDevices));
    }

    private static RankedDevice? SanitizePick(RankedDevice? pick)
        => Sanitize(pick is null ? null : new[] { pick }).FirstOrDefault();

    /// <summary>
    /// Drops entries with a null/empty Id, keeps the first occurrence of each Id,
    /// and replaces a null Name. JSON deserialization can yield nulls despite non-nullable types.
    /// </summary>
    private static IReadOnlyList<RankedDevice> Sanitize(IReadOnlyList<RankedDevice>? raw)
    {
        if (raw is null) return Array.Empty<RankedDevice>();
        var seen = new HashSet<string>();
        var result = new List<RankedDevice>();
        foreach (var d in raw)
        {
            if (d is null || string.IsNullOrEmpty(d.Id) || !seen.Add(d.Id)) continue;
            result.Add(string.IsNullOrEmpty(d.Name) ? d with { Name = UnknownName } : d);
        }
        return result;
    }

    private sealed record FileV1(
        string? SelectedDeviceId,
        string? SelectedPlaybackDeviceId,
        string? SelectedCaptureDeviceId,
        string? SelectedPlaybackDeviceName = null,
        string? SelectedCaptureDeviceName = null,
        IReadOnlyList<RankedDevice>? PlaybackPriority = null,
        IReadOnlyList<RankedDevice>? CapturePriority = null,
        IReadOnlyList<RankedDevice>? KeepAliveDevices = null);

    private sealed record FlowV2(RankedDevice? Pick, IReadOnlyList<RankedDevice>? Priority);

    private sealed record FileV2(
        int Version,
        FlowV2? Playback,
        FlowV2? Recording,
        IReadOnlyList<RankedDevice>? KeepAlive);
}
