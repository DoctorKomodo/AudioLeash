#nullable enable
using Microsoft.Win32;

namespace AudioLeash;

/// <summary>
/// Manages the HKCU\...\Run registry entry that launches AudioLeash at login.
/// </summary>
public sealed class StartupService
{
    private const string DefaultRunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "AudioLeash";

    private readonly string _runKeyPath;

    /// <summary>Production constructor — uses the real Run key.</summary>
    public StartupService() : this(DefaultRunKeyPath) { }

    /// <summary>Test constructor — accepts an injectable key path.</summary>
    internal StartupService(string runKeyPath) => _runKeyPath = runKeyPath;

    /// <summary>True when the AudioLeash Run value exists under the configured key.</summary>
    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath);
            return key?.GetValue(AppName) is not null;
        }
    }

    /// <summary>Writes the Run value pointing to <paramref name="exePath"/>.</summary>
    public void Enable(string exePath)
    {
        // Wrap in quotes so paths containing spaces are parsed correctly by Windows at login.
        string quotedPath = exePath.Contains('"') ? exePath : $"\"{exePath}\"";
        using var key = Registry.CurrentUser.CreateSubKey(_runKeyPath, writable: true);
        key.SetValue(AppName, quotedPath);
    }

    /// <summary>Removes the Run value. Safe to call when already disabled.</summary>
    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
