#nullable enable
using System.Windows.Forms;

namespace AudioLeash;

/// <summary>Helpers that give standard controls the settings window's flat themed look.</summary>
internal static class ThemedControls
{
    /// <summary>Styles <paramref name="button"/> flat with the theme's colours.</summary>
    public static T StyleButton<T>(T button, SettingsTheme theme) where T : ButtonBase
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = theme.Button;
        button.ForeColor = theme.Text;
        button.FlatAppearance.BorderColor        = theme.ButtonBorder;
        button.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
        button.FlatAppearance.MouseDownBackColor = theme.Border;
        return button;
    }
}
