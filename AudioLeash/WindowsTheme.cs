#nullable enable
using System;
using Microsoft.Win32;

namespace AudioLeash;

/// <summary>
/// Reads the Windows personalisation registry key to determine the active colour theme,
/// and surfaces a <see cref="Changed"/> event when the user switches between light and dark.
/// </summary>
internal static class WindowsTheme
{
    private const string PersonalizeKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// Returns <c>true</c> when Windows apps are configured to use the dark theme.
    /// Falls back to <c>false</c> (light) on non-Windows platforms or when the registry
    /// key is absent (i.e. the user has never changed the setting from its default).
    /// </summary>
    internal static bool IsDarkMode
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch (Exception)
            {
                return false; // Registry unavailable (non-Windows or permission denied).
            }
        }
    }

    /// <summary>
    /// Raised when the Windows colour theme changes (e.g. user toggles dark/light in Settings).
    /// Delivered on the <see cref="SystemEvents"/> message-loop thread — in practice the STA/UI
    /// thread when this class is first accessed from that thread.  Route handlers through
    /// <c>SafeInvoke</c> if they touch UI objects as a defensive measure.
    /// </summary>
    internal static event EventHandler? Changed;

    static WindowsTheme()
    {
        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch (Exception ex)
        {
            // SystemEvents not functional on this platform; Changed will never fire.
            System.Diagnostics.Debug.WriteLine(
                $"[AudioLeash] WindowsTheme: could not subscribe to SystemEvents: {ex.Message}");
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
            Changed?.Invoke(null, EventArgs.Empty);
    }
}
