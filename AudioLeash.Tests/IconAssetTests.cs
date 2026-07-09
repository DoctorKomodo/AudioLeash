using System.Drawing;
using System.Drawing.Drawing2D;
using AudioLeash;

namespace AudioLeash.Tests;

public class IconAssetTests
{
    private sealed record Frame(int Width, int Height, byte[] Payload);

    private static byte[] ReadIcon()
    {
        using var stream = typeof(TrayIconLoader).Assembly
            .GetManifestResourceStream("AudioLeash.Resources.icon.ico")!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Parses the ICO directory. We decode frame payloads ourselves rather than
    /// via Icon.ToBitmap(), which is unreliable for PNG-compressed frames.
    /// </summary>
    private static List<Frame> ReadFrames()
    {
        byte[] ico = ReadIcon();
        int count = BitConverter.ToUInt16(ico, 4);
        var frames = new List<Frame>(count);

        for (int i = 0; i < count; i++)
        {
            int entry = 6 + i * 16;
            int width = ico[entry] == 0 ? 256 : ico[entry];
            int height = ico[entry + 1] == 0 ? 256 : ico[entry + 1];
            int length = BitConverter.ToInt32(ico, entry + 8);
            int offset = BitConverter.ToInt32(ico, entry + 12);
            frames.Add(new Frame(width, height, ico.AsSpan(offset, length).ToArray()));
        }

        return frames;
    }

    private static Bitmap Decode(Frame frame)
    {
        Assert.True(
            frame.Payload.Length > 8 && frame.Payload[1] == 'P' &&
            frame.Payload[2] == 'N' && frame.Payload[3] == 'G',
            $"expected a PNG-encoded {frame.Width}px frame; regenerate with tools/generate-icon.py");

        using var stream = new MemoryStream(frame.Payload);
        using var decoded = new Bitmap(stream);

        // A Bitmap constructed from a stream requires that stream to outlive it,
        // so hand back a copy that owns its own pixel data instead.
        return new Bitmap(decoded);
    }

    private static double Luminance(Color c) =>
        (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    [Fact]
    public void Icon_ContainsAllSixSizes()
    {
        int[] sizes = ReadFrames().Select(f => f.Width).OrderBy(w => w).ToArray();

        Assert.Equal(new[] { 16, 32, 48, 64, 128, 256 }, sizes);
    }

    /// <summary>
    /// The 16px frame must be redrawn, not shrunk. A hairline carabiner ring
    /// downscaled from 32px dissolves into partial-alpha grey — the exact defect
    /// this icon replaced. Downscale the 32px frame and assert the real 16px
    /// frame differs by more than resampling noise.
    /// </summary>
    [Fact]
    public void SmallFrame_IsRedrawn_NotDownscaledFromTheLargerFrame()
    {
        var frames = ReadFrames();
        using Bitmap actual = Decode(frames.Single(f => f.Width == 16));
        using Bitmap large = Decode(frames.Single(f => f.Width == 32));

        using var downscaled = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(downscaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(large, 0, 0, 16, 16);
        }

        double total = 0;
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            Color a = actual.GetPixel(x, y);
            Color b = downscaled.GetPixel(x, y);
            total += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G)
                   + Math.Abs(a.B - b.B) + Math.Abs(a.A - b.A);
        }

        double meanAbsDiff = total / (16 * 16 * 4);

        Assert.True(
            meanAbsDiff > DownscaleDifferenceThreshold,
            $"16px frame differs from a downscaled 32px frame by only {meanAbsDiff:F2}; " +
            "it appears to be a plain downscale rather than redrawn artwork.");
    }

    /// <summary>
    /// The glyph must actually be bright and present at tray size — the old icon
    /// failed precisely here, resolving into a pale low-contrast smear.
    /// </summary>
    [Fact]
    public void Glyph_IsBrightAndPresent_At16px()
    {
        using Bitmap frame = Decode(ReadFrames().Single(f => f.Width == 16));

        int opaque = 0, bright = 0;
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            Color c = frame.GetPixel(x, y);
            if (c.A < 128) continue;
            opaque++;
            if (Luminance(c) > 0.80) bright++;
        }

        double fraction = (double)bright / opaque;

        Assert.InRange(fraction, 0.08, 0.35);
    }

    // Measured meanAbsDiff between the real 16px frame and a bilinear
    // downscale of the 32px frame is ~5.60 for the committed artwork
    // (tools/generate-icon.py, sha256 d2ea95f6...). A naive "half of that"
    // threshold (2.8) does not actually discriminate: mutating the generator
    // (SIMPLIFIED_UP_TO = 0, forcing the 16px frame to use unsimplified
    // hairline geometry instead of being redrawn with legible geometry)
    // still produces meanAbsDiff ~4.17, which would pass a 2.8 threshold.
    // 4.9 sits strictly between the two measured values (4.17 mutated,
    // 5.60 committed), verified by re-running the mutation with this
    // threshold in place (see task-3-report.md).
    private const double DownscaleDifferenceThreshold = 4.9;
}
