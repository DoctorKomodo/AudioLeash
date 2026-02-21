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

    public string? LoadSelectedDeviceId()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return settings?.SelectedDeviceId;
        }
        catch
        {
            return null;
        }
    }

    public void SaveSelectedDeviceId(string? id)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var settings = new AppSettings(id);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch { /* best-effort — do not crash the app over persistence */ }
    }

    private sealed record AppSettings(string? SelectedDeviceId);
}
