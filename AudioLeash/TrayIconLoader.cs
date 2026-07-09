#nullable enable
using System.Drawing;
using System.Reflection;

namespace AudioLeash;

/// <summary>
/// Loads the embedded tray icon.
/// </summary>
public static class TrayIconLoader
{
    private const string ResourceName = "AudioLeash.Resources.icon.ico";

    /// <summary>
    /// Loads the icon frame matching <paramref name="desired"/>.
    /// </summary>
    /// <remarks>
    /// The parameterless <c>Icon(Stream)</c> constructor selects the .ico's
    /// default frame — 32x32 — and leaves NotifyIcon to downscale it to tray
    /// size, which blurs the hand-tuned 16px artwork. Passing the desired size
    /// makes GDI+ pick the matching frame instead.
    /// </remarks>
    /// <returns>
    /// A caller-owned <see cref="Icon"/> that must be disposed, on both the
    /// success path and the fallback path. The fallback returns an
    /// independently-owned clone of <see cref="SystemIcons.Application"/>
    /// rather than that shared static instance, so disposing it never affects
    /// the process-wide default icon.
    /// </returns>
    public static Icon Load(Size desired)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(ResourceName);
            if (stream is not null)
                return new Icon(stream, desired);
        }
        catch { /* fall through to the system default */ }

        return (Icon)SystemIcons.Application.Clone();
    }
}
