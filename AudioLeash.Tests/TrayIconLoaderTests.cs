using System.Drawing;
using System.Reflection;
using AudioLeash;

namespace AudioLeash.Tests;

public class TrayIconLoaderTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(48)]
    public void Load_SelectsTheFrameMatchingTheRequestedSize(int px)
    {
        using var icon = TrayIconLoader.Load(new Size(px, px));

        Assert.Equal(new Size(px, px), icon.Size);
    }

    /// <summary>
    /// Documents why <see cref="TrayIconLoader"/> exists: the parameterless
    /// Icon(Stream) constructor selects the .ico's default frame (32x32), not
    /// the 16x16 frame the tray needs. NotifyIcon then downscales it, blurring
    /// whatever pixel-tuned artwork the 16px frame contains.
    /// </summary>
    [Fact]
    public void DefaultIconConstructor_DoesNotSelectTheSmallestFrame()
    {
        using var stream = typeof(TrayIconLoader).Assembly
            .GetManifestResourceStream("AudioLeash.Resources.icon.ico")!;
        using var icon = new Icon(stream);

        Assert.NotEqual(new Size(16, 16), icon.Size);
    }
}
