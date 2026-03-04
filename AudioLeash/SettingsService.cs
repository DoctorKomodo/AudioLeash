#nullable enable
using System;
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
        var existing = LoadSettings();
        SaveSettings(new AppSettings(
            SelectedDeviceId: null,  // clear legacy field
            SelectedPlaybackDeviceId: id,
            SelectedCaptureDeviceId: existing?.SelectedCaptureDeviceId));
    }

    /// <summary>
    /// Persists the capture (recording) device ID, preserving any existing playback selection.
    /// Migrates the legacy <c>SelectedDeviceId</c> to the playback field if present.
    /// </summary>
    public void SaveSelectedCaptureDeviceId(string? id)
    {
        var existing = LoadSettings();
        SaveSettings(new AppSettings(
            SelectedDeviceId: null,  // clear legacy field
            SelectedPlaybackDeviceId: existing?.SelectedPlaybackDeviceId
                                     ?? existing?.SelectedDeviceId,
            SelectedCaptureDeviceId: id));
    }

    /// <summary>
    /// Persists both playback and capture device IDs in a single write.
    /// Unlike the single-field save methods, this does not merge with existing values.
    /// </summary>
    public void SaveSelectedDeviceIds(string? playbackId, string? captureId)
    {
        SaveSettings(new AppSettings(
            SelectedDeviceId: null,
            SelectedPlaybackDeviceId: playbackId,
            SelectedCaptureDeviceId: captureId));
    }

    // ── Internal helpers ────────────────────────────────────────────────

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
        string? SelectedCaptureDeviceId);
}
