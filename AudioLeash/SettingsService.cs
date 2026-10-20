#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AudioLeash;

/// <summary>
/// Persists user settings to %AppData%\AudioLeash\settings.json.
/// All operations are best-effort: failures are silently swallowed.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _directory;
    private string FilePath => Path.Combine(_directory, "settings.json");

    /// <summary>Production constructor — uses %AppData%\AudioLeash\.</summary>
    public SettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioLeash"))
    { }

    /// <summary>Test constructor — uses the provided directory.</summary>
    internal SettingsService(string directory) => _directory = directory;

    /// <summary>
    /// Returns <c>true</c> if the settings file exists on disk, regardless of its contents.
    /// Use this to distinguish a genuine first run (no file) from an explicit "no device" state
    /// (file exists but <see cref="LoadSelectedPlaybackDeviceId"/> returned <c>null</c>).
    /// </summary>
    public bool HasSettingsFile => File.Exists(FilePath);

    // ── Legacy API (kept for backward compatibility during migration) ──

    /// <summary>
    /// Loads the selected device ID from the old single-field format.
    /// Delegates to <see cref="LoadSelectedPlaybackDeviceId"/> for the new format.
    /// </summary>
    public string? LoadSelectedDeviceId() => LoadSelectedPlaybackDeviceId();

    /// <summary>
    /// Saves using the old single-field name. Delegates to the new API,
    /// preserving any existing capture selection.
    /// </summary>
    public void SaveSelectedDeviceId(string? id) => SaveSelectedPlaybackDeviceId(id);

    // ── New API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the persisted playback device ID, or <c>null</c> if none is saved.
    /// Falls back to the legacy <c>SelectedDeviceId</c> field for backward compatibility.
    /// </summary>
    public string? LoadSelectedPlaybackDeviceId()
    {
        var settings = LoadSettings();
        // New field takes precedence; fall back to legacy field for migration
        return settings?.SelectedPlaybackDeviceId ?? settings?.SelectedDeviceId;
    }

    /// <summary>
    /// Returns the persisted capture (recording) device ID, or <c>null</c> if none is saved.
    /// </summary>
    public string? LoadSelectedCaptureDeviceId()
    {
        return LoadSettings()?.SelectedCaptureDeviceId;
    }

    /// <summary>
    /// Persists the playback device ID, preserving any existing capture selection.
    /// Clears the legacy <c>SelectedDeviceId</c> field.
    /// </summary>
    public void SaveSelectedPlaybackDeviceId(string? id)
    {
        var existing = LoadSettings() ?? Empty;
        SaveSettings(existing with
        {
            SelectedDeviceId = null,  // clear legacy field
            SelectedPlaybackDeviceId = id,
            SelectedPlaybackDeviceName = id is null ? null : existing.SelectedPlaybackDeviceName,
        });
    }

    /// <summary>
    /// Persists the capture (recording) device ID, preserving any existing playback selection.
    /// Migrates the legacy <c>SelectedDeviceId</c> to the playback field if present.
    /// </summary>
    public void SaveSelectedCaptureDeviceId(string? id)
    {
        var existing = LoadSettings() ?? Empty;
        SaveSettings(existing with
        {
            SelectedDeviceId = null,  // clear legacy field
            SelectedPlaybackDeviceId = existing.SelectedPlaybackDeviceId ?? existing.SelectedDeviceId,
            SelectedCaptureDeviceId = id,
            SelectedCaptureDeviceName = id is null ? null : existing.SelectedCaptureDeviceName,
        });
    }

    /// <summary>
    /// Persists both playback and capture device IDs in a single write.
    /// Unlike the single-field save methods, this does not merge with existing values.
    /// </summary>
    public void SaveSelectedDeviceIds(string? playbackId, string? captureId)
    {
        SaveSettings((LoadSettings() ?? Empty) with
        {
            SelectedDeviceId = null,
            SelectedPlaybackDeviceId = playbackId,
            SelectedCaptureDeviceId = captureId,
            SelectedPlaybackDeviceName = null,
            SelectedCaptureDeviceName = null,
        });
    }

    // ── Device name persistence ─────────────────────────────────────────

    /// <summary>
    /// Returns the persisted playback device friendly name, or <c>null</c> if none is saved.
    /// </summary>
    public string? LoadSelectedPlaybackDeviceName()
        => LoadSettings()?.SelectedPlaybackDeviceName;

    /// <summary>
    /// Returns the persisted capture device friendly name, or <c>null</c> if none is saved.
    /// </summary>
    public string? LoadSelectedCaptureDeviceName()
        => LoadSettings()?.SelectedCaptureDeviceName;

    /// <summary>
    /// Persists the playback device ID and friendly name, preserving any existing capture selection.
    /// </summary>
    public void SaveSelectedPlaybackDevice(string? id, string? name)
    {
        SaveSettings((LoadSettings() ?? Empty) with
        {
            SelectedDeviceId = null,
            SelectedPlaybackDeviceId = id,
            SelectedPlaybackDeviceName = name,
        });
    }

    /// <summary>
    /// Persists the capture device ID and friendly name, preserving any existing playback selection.
    /// </summary>
    public void SaveSelectedCaptureDevice(string? id, string? name)
    {
        var existing = LoadSettings() ?? Empty;
        SaveSettings(existing with
        {
            SelectedDeviceId = null,
            SelectedPlaybackDeviceId = existing.SelectedPlaybackDeviceId ?? existing.SelectedDeviceId,
            SelectedCaptureDeviceId = id,
            SelectedCaptureDeviceName = name,
        });
    }

    // ── Device priority lists ───────────────────────────────────────────

    /// <summary>Returns the ranked playback list (sanitized); empty if none is saved.</summary>
    public IReadOnlyList<RankedDevice> LoadPlaybackPriority()
        => Sanitize(LoadSettings()?.PlaybackPriority);

    /// <summary>Returns the ranked capture list (sanitized); empty if none is saved.</summary>
    public IReadOnlyList<RankedDevice> LoadCapturePriority()
        => Sanitize(LoadSettings()?.CapturePriority);

    /// <summary>Persists the ranked playback list, preserving all other fields.</summary>
    public void SavePlaybackPriority(IReadOnlyList<RankedDevice> list)
        => SaveSettings((LoadSettings() ?? Empty) with { PlaybackPriority = list });

    /// <summary>Persists the ranked capture list, preserving all other fields.</summary>
    public void SaveCapturePriority(IReadOnlyList<RankedDevice> list)
        => SaveSettings((LoadSettings() ?? Empty) with { CapturePriority = list });

    /// <summary>
    /// Persists the ranked playback list and the playback pick (id and name) in a single
    /// write, preserving the capture flow. Clears the legacy <c>SelectedDeviceId</c> field.
    /// </summary>
    public void SavePlaybackFlow(IReadOnlyList<RankedDevice> list, string? pickId, string? pickName)
    {
        SaveSettings((LoadSettings() ?? Empty) with
        {
            SelectedDeviceId = null,
            SelectedPlaybackDeviceId = pickId,
            SelectedPlaybackDeviceName = pickName,
            PlaybackPriority = list,
        });
    }

    /// <summary>
    /// Persists the ranked capture list and the capture pick (id and name) in a single
    /// write, preserving the playback flow. Migrates the legacy <c>SelectedDeviceId</c>
    /// to the playback field if present.
    /// </summary>
    public void SaveCaptureFlow(IReadOnlyList<RankedDevice> list, string? pickId, string? pickName)
    {
        var existing = LoadSettings() ?? Empty;
        SaveSettings(existing with
        {
            SelectedDeviceId = null,
            SelectedPlaybackDeviceId = existing.SelectedPlaybackDeviceId ?? existing.SelectedDeviceId,
            SelectedCaptureDeviceId = pickId,
            SelectedCaptureDeviceName = pickName,
            CapturePriority = list,
        });
    }

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
            result.Add(string.IsNullOrEmpty(d.Name) ? d with { Name = "Unknown device" } : d);
        }
        return result;
    }

    // ── Internal helpers ────────────────────────────────────────────────

    private static readonly AppSettings Empty = new(null, null, null);

    private AppSettings? LoadSettings()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void SaveSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception) { /* best-effort */ }
    }

    private sealed record AppSettings(
        string? SelectedDeviceId,
        string? SelectedPlaybackDeviceId,
        string? SelectedCaptureDeviceId,
        string? SelectedPlaybackDeviceName = null,
        string? SelectedCaptureDeviceName = null,
        IReadOnlyList<RankedDevice>? PlaybackPriority = null,
        IReadOnlyList<RankedDevice>? CapturePriority = null);
}
