#nullable enable
using System.Windows.Forms;

namespace AudioLeash;

/// <summary>Helpers that give standard controls the settings window's flat themed look.</summary>
internal static class ThemedControls
{
    /// <summary>
    /// Styles <paramref name="button"/> flat with the theme's colours. A primary button
    /// (Save) uses the accent fill, as Windows 11 dialogs do for their default action.
    /// </summary>
    public static T StyleButton<T>(T button, SettingsTheme theme, bool primary = false) where T : ButtonBase
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = primary ? theme.Accent : theme.Button;
        button.ForeColor = primary ? theme.AccentText : theme.Text;
        button.FlatAppearance.BorderColor        = primary ? theme.Accent : theme.ButtonBorder;
        button.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Light(theme.Accent, 0.15f) : theme.ButtonHover;
        button.FlatAppearance.MouseDownBackColor = primary ? ControlPaint.Dark(theme.Accent, 0.05f) : theme.Border;
        return button;
    }
}
