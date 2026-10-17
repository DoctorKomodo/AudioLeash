#nullable enable
using System.Drawing;

namespace AudioLeash;

/// <summary>
/// Colours for the settings window, matching the Windows 11 light or dark palette.
/// The window draws its own tabs, list and buttons with these rather than relying on
/// the classic controls' theming, which doesn't follow dark mode (e.g. <c>TabControl</c>).
/// </summary>
internal sealed record SettingsTheme(
    Color Window,
    Color Surface,
    Color Border,
    Color Text,
    Color MutedText,
    Color Accent,
    Color AccentText,
    Color Selection,
    Color Footer,
    Color FooterBorder,
    Color Button,
    Color ButtonHover,
    Color ButtonBorder,
    Color Connected,
    Color Disconnected)
{
    public static readonly SettingsTheme Dark = new(
        Window:       Color.FromArgb(0x20, 0x20, 0x20),
        Surface:      Color.FromArgb(0x2B, 0x2B, 0x2B),
        Border:       Color.FromArgb(0x3A, 0x3A, 0x3A),
        Text:         Color.FromArgb(0xE8, 0xE8, 0xE8),
        MutedText:    Color.FromArgb(0x9A, 0x9A, 0x9A),
        Accent:       Color.FromArgb(0x4C, 0xC2, 0xFF),
        AccentText:   Color.Black,
        Selection:    Color.FromArgb(0x3A, 0x3A, 0x3A),
        Footer:       Color.FromArgb(0x1A, 0x1A, 0x1A),
        FooterBorder: Color.FromArgb(0x2E, 0x2E, 0x2E),
        Button:       Color.FromArgb(0x2D, 0x2D, 0x2D),
        ButtonHover:  Color.FromArgb(0x38, 0x38, 0x38),
        ButtonBorder: Color.FromArgb(0x45, 0x45, 0x45),
        Connected:    Color.FromArgb(0x3F, 0xB9, 0x50),
        Disconnected: Color.FromArgb(0x77, 0x77, 0x77));

    public static readonly SettingsTheme Light = new(
        Window:       Color.FromArgb(0xF3, 0xF3, 0xF3),
        Surface:      Color.White,
        Border:       Color.FromArgb(0xD6, 0xD6, 0xD6),
        Text:         Color.FromArgb(0x1B, 0x1B, 0x1B),
        MutedText:    Color.FromArgb(0x60, 0x60, 0x60),
        Accent:       Color.FromArgb(0x00, 0x67, 0xC0),
        AccentText:   Color.White,
        Selection:    Color.FromArgb(0xE5, 0xF0, 0xFB),
        Footer:       Color.FromArgb(0xEA, 0xEA, 0xEA),
        FooterBorder: Color.FromArgb(0xDC, 0xDC, 0xDC),
        Button:       Color.FromArgb(0xFB, 0xFB, 0xFB),
        ButtonHover:  Color.FromArgb(0xF0, 0xF0, 0xF0),
        ButtonBorder: Color.FromArgb(0xD0, 0xD0, 0xD0),
        Connected:    Color.FromArgb(0x0F, 0x7B, 0x0F),
        Disconnected: Color.FromArgb(0x8A, 0x8A, 0x8A));

    /// <summary>The palette for the current Windows app theme.</summary>
    public static SettingsTheme Current => WindowsTheme.IsDarkMode ? Dark : Light;
}
