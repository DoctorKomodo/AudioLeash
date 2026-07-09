using System.Drawing;
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
    /// The 16px frame must be redrawn, not shrunk. This asserts the design
    /// intent directly: the glyph resolves into exactly two bright masses — the
    /// speaker and the carabiner ring — separated by tile pixels, and its white
    /// is really white rather than antialiased grey.
    /// </summary>
    /// <remarks>
    /// An earlier version compared the 16px frame against a bilinear downscale
    /// of the 32px frame. That metric separated good artwork from bad by only
    /// 1.34x, because both are independent LANCZOS reductions of a supersampled
    /// render and differ substantially either way. These two assertions separate
    /// by 3.4x: regenerating with SIMPLIFIED_UP_TO = 0 (all frames from the full
    /// hairline geometry) yields 5 fragmented masses and 8 near-white pixels,
    /// against 2 masses and 27 near-white pixels for the real artwork.
    /// </remarks>
    [Fact]
    public void SmallFrame_ResolvesIntoTwoCrispMasses()
    {
        using Bitmap frame = Decode(ReadFrames().Single(f => f.Width == 16));

        int nearWhite = 0;
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            Color c = frame.GetPixel(x, y);
            if (c.A >= 128 && Luminance(c) > 0.95) nearWhite++;
        }

        Assert.True(
            nearWhite >= MinimumNearWhitePixels,
            $"only {nearWhite} near-white pixels at 16px (need >= {MinimumNearWhitePixels}); " +
            "the glyph has dissolved into antialiased grey — is it being downscaled?");

        Assert.Equal(2, CountBrightMasses(frame));
    }

    /// <summary>
    /// Flood-fills the bright (glyph) pixels into 4-connected components. The
    /// speaker and the ring must remain two separate masses: if they merge the
    /// icon reads as one blob, and if they fragment it has dissolved.
    /// </summary>
    private static int CountBrightMasses(Bitmap frame)
    {
        bool IsBright(int x, int y)
        {
            Color c = frame.GetPixel(x, y);
            return c.A >= 128 && Luminance(c) > 0.80;
        }

        var seen = new bool[16, 16];
        int masses = 0;

        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            if (seen[x, y] || !IsBright(x, y)) continue;

            masses++;
            var stack = new Stack<(int X, int Y)>();
            stack.Push((x, y));

            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Pop();
                if (cx < 0 || cx > 15 || cy < 0 || cy > 15) continue;
                if (seen[cx, cy] || !IsBright(cx, cy)) continue;

                seen[cx, cy] = true;
                stack.Push((cx + 1, cy));
                stack.Push((cx - 1, cy));
                stack.Push((cx, cy + 1));
                stack.Push((cx, cy - 1));
            }
        }

        return masses;
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

    // Real artwork scores 27; the hairline regression scores 8.
    private const int MinimumNearWhitePixels = 20;
}
