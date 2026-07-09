# Icon Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace AudioLeash's tray icon with one that is legible at 16x16 and reads as a sibling of the AudioStreamer icon, and fix the loader so the tray actually displays the 16px frame.

**Architecture:** A committed Python/Pillow script (`tools/generate-icon.py`) renders six frames — 16, 32, 48, 64, 128, 256 — and writes them into a single `.ico`. Small frames (16, 32) use *redrawn, simplified* artwork; they are never downscaled from a larger frame. On the C# side, icon loading moves out of `AudioLeashContext` into a testable `TrayIconLoader` that selects the frame matching a requested size.

**Tech Stack:** C# 14 / .NET 10, WinForms, xUnit, `System.Drawing` (tests decode frames); Python 3 + Pillow (dev-time only, not part of the build).

**Spec:** `docs/superpowers/specs/2026-07-09-icon-redesign-design.md`

## Global Constraints

- Target framework: app `net10.0-windows`, tests `net10.0-windows7.0`. Windows only.
- `#nullable enable` — all new C# must be null-safe.
- Dispose all `IDisposable` — `Icon`, `Bitmap`, `MemoryStream`.
- Pillow is a **dev-time-only** dependency. It must not be added to any `.csproj` and must not be wired into the build.
- The single `AudioLeash/Resources/icon.ico` serves all three consumers. Do **not** modify `AudioLeash.csproj` or `installer/AudioLeash.iss`.
- Tile geometry, copied verbatim from the spec: corner radius `0.153` of tile width; gradient `#22C3DE` (top) → `#0E7C96` (bottom); glyph pure white `#FFFFFF`.
- `.ico` must contain exactly these six sizes: 16, 32, 48, 64, 128, 256.
- Conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`). Work stays on branch `feature/new-icon`.
- Run `dotnet test AudioLeash.sln` before any commit that touches C#.

---

### Task 1: Extract `TrayIconLoader` and select the small frame

Establishes empirically that `new Icon(stream)` does *not* return the 16px frame, then fixes it. If Step 2 shows the premise is false — the default constructor already yields 16x16 — **stop, delete `TrayIconLoaderTests.DefaultIconConstructor_DoesNotSelectTheSmallestFrame`, and report to the user.** Do not ship a change that does nothing.

**Files:**
- Create: `AudioLeash/TrayIconLoader.cs`
- Modify: `AudioLeash/AudioLeashContext.cs` (the `Icon = GetTrayIcon(),` initialiser near line 43, and the `GetTrayIcon` method near line 610)
- Test: `AudioLeash.Tests/TrayIconLoaderTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `public static class AudioLeash.TrayIconLoader` with `public static Icon Load(Size desired)`. Task 3 reuses its assembly to reach the embedded resource.

- [ ] **Step 1: Write the failing test**

Create `AudioLeash.Tests/TrayIconLoaderTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test AudioLeash.sln --filter FullyQualifiedName~TrayIconLoaderTests`

Expected: **compile error**, `CS0103: The name 'TrayIconLoader' does not exist in the current context`.

Now verify the premise before writing the fix. Temporarily comment out the two `TrayIconLoader` references (the `[Theory]` and the `typeof(TrayIconLoader)`, replacing the latter with `typeof(AudioLeashContext)`), then run only `DefaultIconConstructor_DoesNotSelectTheSmallestFrame`.

Expected: **PASS** — confirming `new Icon(stream).Size` is not 16x16.

If it **fails** (the default is already 16x16), the premise is wrong. Stop and report; the loader change is unnecessary. Otherwise restore the file and continue.

- [ ] **Step 3: Write the minimal implementation**

Create `AudioLeash/TrayIconLoader.cs`:

```csharp
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

        return SystemIcons.Application;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test AudioLeash.sln --filter FullyQualifiedName~TrayIconLoaderTests`

Expected: PASS, 4 tests.

- [ ] **Step 5: Wire it into `AudioLeashContext`**

In `AudioLeash/AudioLeashContext.cs`, change the `NotifyIcon` initialiser:

```csharp
// before
Icon             = GetTrayIcon(),
// after
Icon             = TrayIconLoader.Load(SystemInformation.SmallIconSize),
```

Then delete the now-unused `GetTrayIcon` method entirely:

```csharp
    private static Icon GetTrayIcon()
    {
        try
        {
            using var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("AudioLeash.Resources.icon.ico");
            if (stream is not null)
                return new Icon(stream);
        }
        catch { /* fall through to default */ }
        return SystemIcons.Application;
    }
```

`SystemInformation.SmallIconSize` is the tray's icon size for the current DPI. On a 100% display it is 16x16; at higher scaling GDI+ picks the nearest available frame.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test AudioLeash.sln`

Expected: PASS. 52 existing tests + 4 new = 56, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add AudioLeash/TrayIconLoader.cs AudioLeash/AudioLeashContext.cs AudioLeash.Tests/TrayIconLoaderTests.cs
git commit -m "fix: load the 16px icon frame for the tray

new Icon(stream) selects the .ico's default 32x32 frame and lets NotifyIcon
downscale it, blurring the tray icon. Extract loading into TrayIconLoader and
request SystemInformation.SmallIconSize so GDI+ picks the matching frame."
```

---

### Task 2: Generate the new icon

Produces the artwork. The `.ico` is a binary asset, so this task's gate is a **visual** check, not an assertion; Task 3 adds the regression tests that lock the result in.

**Files:**
- Create: `tools/generate-icon.py`
- Modify (regenerate): `AudioLeash/Resources/icon.ico`

**Interfaces:**
- Consumes: nothing.
- Produces: `AudioLeash/Resources/icon.ico` containing six frames — 16, 32, 48, 64, 128, 256 — each PNG-encoded, with 16 and 32 drawn from simplified geometry.

- [ ] **Step 1: Confirm Pillow is available**

Run: `python -c "import PIL; print(PIL.__version__)"`

Expected: `8.1.0` or later — `append_images` support for ICO landed in 8.1.0. If Pillow is missing: `python -m pip install Pillow`.

- [ ] **Step 2: Write the generator**

Create `tools/generate-icon.py`:

```python
#!/usr/bin/env python3
"""Regenerate AudioLeash/Resources/icon.ico.

The icon is a rounded-square cyan tile carrying a white speaker-plus-carabiner
glyph, matching the AudioStreamer icon's tile geometry so the two apps read as
siblings.

Small frames (16, 32) are drawn from SIMPLE geometry, not downscaled from a
large one: at 16px a hairline carabiner ring dissolves into grey mush. Every
frame is supersampled 8x and reduced with LANCZOS; the small frames have their
coordinates snapped to the device pixel grid first.

Usage:
    python tools/generate-icon.py            # write the .ico
    python tools/generate-icon.py --preview  # also dump PNGs for inspection
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw

SS = 8                       # supersampling factor
SIZES = [16, 32, 48, 64, 128, 256]
SIMPLIFIED_UP_TO = 32        # sizes <= this use the SIMPLE geometry

GRADIENT_TOP = (0x22, 0xC3, 0xDE)
GRADIENT_BOTTOM = (0x0E, 0x7C, 0x96)
CORNER_RADIUS_FRAC = 0.153   # measured from the AudioStreamer icon

WHITE = (255, 255, 255, 255)
CLEAR = (255, 255, 255, 0)

# All coordinates are fractions of the tile's width.
FULL = {
    "plate": (0.265, 0.415, 0.355, 0.585),
    "cone": ((0.355, 0.415), (0.495, 0.285), (0.495, 0.715), (0.355, 0.585)),
    "bar": (0.495, 0.470, 0.585, 0.530),
    "ring_center": (0.685, 0.500),
    "ring_size": (0.175, 0.300),
    "wall": 0.042,
    "cant": 20.0,
    "gate": True,
    "cutout": True,
}

SIMPLE = {
    "plate": (0.220, 0.380, 0.340, 0.620),
    "cone": ((0.340, 0.380), (0.500, 0.240), (0.500, 0.760), (0.340, 0.620)),
    "bar": (0.500, 0.445, 0.580, 0.555),
    "ring_center": (0.690, 0.500),
    "ring_size": (0.210, 0.350),
    "wall": 0.075,
    "cant": 0.0,
    "gate": False,
    "cutout": True,
}


def _tile(n: int) -> Image.Image:
    """The rounded-square gradient tile, n x n pixels."""
    column = Image.new("RGB", (1, n))
    for y in range(n):
        t = y / (n - 1)
        column.putpixel(
            (0, y),
            tuple(round(a + (b - a) * t) for a, b in zip(GRADIENT_TOP, GRADIENT_BOTTOM)),
        )
    base = column.resize((n, n), Image.NEAREST).convert("RGBA")

    mask = Image.new("L", (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, n - 1, n - 1], radius=CORNER_RADIUS_FRAC * n, fill=255
    )
    base.putalpha(mask)
    return base


def _glyph(n: int, spec: dict, snap: int) -> Image.Image:
    """The white speaker + carabiner, n x n pixels, transparent background.

    `snap` > 0 rounds every coordinate to that many supersampled pixels, so
    edges land on whole device pixels after reduction.
    """

    def p(fraction: float) -> float:
        value = fraction * n
        return round(value / snap) * snap if snap else value

    layer = Image.new("RGBA", (n, n), CLEAR)
    draw = ImageDraw.Draw(layer)

    draw.rectangle([p(v) for v in spec["plate"]], fill=WHITE)
    draw.polygon([(p(x), p(y)) for x, y in spec["cone"]], fill=WHITE)
    draw.rectangle([p(v) for v in spec["bar"]], fill=WHITE)

    # The carabiner is drawn on its own layer so it can be canted about its
    # own centre without rotating the speaker.
    ring = Image.new("RGBA", (n, n), CLEAR)
    rd = ImageDraw.Draw(ring)

    cx, cy = p(spec["ring_center"][0]), p(spec["ring_center"][1])
    rw, rh = p(spec["ring_size"][0]), p(spec["ring_size"][1])
    x0, y0, x1, y1 = cx - rw / 2, cy - rh / 2, cx + rw / 2, cy + rh / 2

    rd.rounded_rectangle([x0, y0, x1, y1], radius=rw / 2, fill=WHITE)

    wall = p(spec["wall"])
    if spec["cutout"]:
        rd.rounded_rectangle(
            [x0 + wall, y0 + wall, x1 - wall, y1 - wall],
            radius=max(rw / 2 - wall, 1),
            fill=CLEAR,
        )
    if spec["gate"]:
        gate_y = y0 + rh * 0.30
        rd.line(
            [(x0 - wall * 0.25, gate_y), (x1 + wall * 0.25, gate_y)],
            fill=WHITE,
            width=max(int(wall * 0.9), 1),
        )
    if spec["cant"]:
        ring = ring.rotate(-spec["cant"], resample=Image.BICUBIC, center=(cx, cy))

    return Image.alpha_composite(layer, ring)


def render(size: int) -> Image.Image:
    """One fully-composited frame at `size` x `size`."""
    simplified = size <= SIMPLIFIED_UP_TO
    spec = dict(SIMPLE if simplified else FULL)
    if size == 16:
        # A 1px ring wall vanishes; a solid pill still reads as a clip.
        spec["cutout"] = False

    n = size * SS
    composed = Image.alpha_composite(_tile(n), _glyph(n, spec, SS if simplified else 0))
    return composed.resize((size, size), Image.LANCZOS)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--preview", action="store_true", help="also dump PNGs")
    parser.add_argument("--out", type=Path, default=None)
    args = parser.parse_args()

    repo = Path(__file__).resolve().parent.parent
    out = args.out or repo / "AudioLeash" / "Resources" / "icon.ico"

    frames = {size: render(size) for size in SIZES}

    # Pillow's ICO writer matches append_images by exact size and embeds them
    # verbatim; only sizes with no matching image get thumbnail()-downscaled.
    # Supplying all six means none is ever downscaled.
    frames[256].save(
        out,
        format="ICO",
        sizes=[(s, s) for s in SIZES],
        append_images=[frames[s] for s in SIZES if s != 256],
    )
    print(f"wrote {out} ({out.stat().st_size} bytes)")

    if args.preview:
        preview_dir = repo / "build" / "icon-preview"
        preview_dir.mkdir(parents=True, exist_ok=True)
        for size, image in frames.items():
            image.save(preview_dir / f"icon_{size}.png")
            image.resize((size * 10, size * 10), Image.NEAREST).save(
                preview_dir / f"icon_{size}_zoom.png"
            )
        print(f"previews in {preview_dir}")


if __name__ == "__main__":
    main()
```

- [ ] **Step 3: Render previews and look at them**

Run: `python tools/generate-icon.py --preview --out build/icon-preview/candidate.ico`

Then **open `build/icon-preview/icon_16_zoom.png` and `icon_256.png` and actually look at them.** Check, in order:

1. At 16px, is the carabiner distinguishable from the speaker — two separate masses, not one blob?
2. At 16px, is the speaker still recognisable as a speaker?
3. At 256px, does the canted carabiner read as hanging, not as a letter O?
4. Do the tile's rounded corners match AudioStreamer's when the two 256px frames sit side by side?

- [ ] **Step 4: Tune the constants until it passes the visual check**

The geometry constants are a starting point, not a finished design. Expect two or three rounds. Adjust only the `FULL` / `SIMPLE` dicts and re-run Step 3.

Known levers, in the order most likely to be needed:

- **Blob at 16px** → increase the gap between speaker and ring: lower `SIMPLE["bar"][2]` (the bar's right edge) and raise `SIMPLE["ring_center"][0]`.
- **Ring too faint at 16px** → increase `SIMPLE["ring_size"]`; the 16px frame already has `cutout` forced off.
- **Ring reads as a pill, not a clip, at 32px** → increase `SIMPLE["wall"]` so the cutout is a thin slot rather than a wide hole.
- **Speaker cone looks bent** → the cone's first and last points must exactly equal the plate's right-hand corners.

Do not proceed until the four visual checks pass. If after several rounds 16px still will not resolve two masses, fall back to dropping the link bar entirely at 16px — the speaker and ring alone, with clear space between them.

- [ ] **Step 5: Write the real icon**

Run: `python tools/generate-icon.py`

Expected: `wrote .../AudioLeash/Resources/icon.ico (NNNNN bytes)`.

- [ ] **Step 6: Verify the frames are what we think they are**

Run:

```bash
python -c "
from PIL import Image
im = Image.open(r'AudioLeash/Resources/icon.ico')
print('sizes:', sorted(im.ico.sizes()))
"
```

Expected: `sizes: [(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]`

- [ ] **Step 7: Confirm the app builds and looks right in the tray**

Run: `dotnet build AudioLeash.sln -c Release`, then launch the app and look at the notification area.

Expected: the tray shows a cyan tile with a discernible speaker and carabiner. Compare against AudioStreamer's icon if it is also running.

- [ ] **Step 8: Commit**

`build/` is a scratch directory for previews — confirm it is ignored before committing (`git status --short`). If it appears, add `build/` to `.gitignore` in this commit.

```bash
git add tools/generate-icon.py AudioLeash/Resources/icon.ico
git commit -m "feat: redesign the icon for legibility at tray size

Cyan rounded-square tile with a white speaker-and-carabiner glyph, matching
the AudioStreamer icon's tile geometry so the two read as siblings. The 16px
and 32px frames are drawn from simplified geometry rather than downscaled, so
the carabiner survives at tray size.

tools/generate-icon.py is the source of truth and reproduces the .ico."
```

---

### Task 3: Lock the icon in with regression tests

Without these, a future regeneration could quietly go back to downscaling one master and nobody would notice until the tray looked bad again.

**Files:**
- Create: `AudioLeash.Tests/IconAssetTests.cs`

**Interfaces:**
- Consumes: `AudioLeash.TrayIconLoader` (Task 1) — used only to reach the app assembly. `AudioLeash/Resources/icon.ico` (Task 2).
- Produces: nothing consumed downstream.

- [ ] **Step 1: Write the failing tests**

Create `AudioLeash.Tests/IconAssetTests.cs`:

```csharp
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

    private const double DownscaleDifferenceThreshold = 12.0;
}
```

- [ ] **Step 2: Run the tests and calibrate the threshold**

Run: `dotnet test AudioLeash.sln --filter FullyQualifiedName~IconAssetTests`

`DownscaleDifferenceThreshold` is a **placeholder value that must be calibrated against the real artwork**, not accepted as-is.

- `Icon_ContainsAllSixSizes` must pass.
- If `SmallFrame_IsRedrawn...` fails, read the reported `meanAbsDiff`. If it is a healthy margin above zero (say above 20), set the threshold to roughly **half** the measured value and re-run. If it is genuinely small (under 5), the simplified geometry is too close to the full geometry — go back to Task 2 Step 4 and differentiate the 16px artwork further. **Do not lower the threshold to force a pass; that defeats the test.**
- If `Glyph_IsBrightAndPresent_At16px` fails, print `fraction`. Below 0.08 means the glyph is too small or too dim at 16px; above 0.35 means it floods the tile. Fix the artwork in Task 2, not the bounds.

- [ ] **Step 3: Run the whole suite**

Run: `dotnet test AudioLeash.sln`

Expected: PASS. 56 + 3 = 59 tests, 0 failed.

- [ ] **Step 4: Prove the downscale test actually catches the defect**

A regression test nobody has seen fail is a test you do not know works. Temporarily set `SIMPLIFIED_UP_TO = 0` in `tools/generate-icon.py` (so all frames use `FULL` geometry and 16px becomes effectively the hairline design), regenerate, and re-run.

Run: `python tools/generate-icon.py && dotnet test AudioLeash.sln --filter FullyQualifiedName~IconAssetTests`

Expected: `SmallFrame_IsRedrawn_NotDownscaledFromTheLargerFrame` **FAILS**, and quite possibly `Glyph_IsBrightAndPresent_At16px` too.

Then revert `SIMPLIFIED_UP_TO` to `32`, regenerate, and confirm both pass again. Verify `git status --short` shows `icon.ico` unmodified — the generator is deterministic, so regeneration must reproduce the committed bytes exactly. If it does not, the generator has a non-deterministic input and that must be fixed before proceeding.

- [ ] **Step 5: Commit**

```bash
git add AudioLeash.Tests/IconAssetTests.cs
git commit -m "test: guard icon legibility at tray size

Assert the .ico carries all six frames, that the 16px frame is redrawn rather
than downscaled from the 32px one, and that the glyph stays bright against the
tile at 16px."
```

---

### Task 4: Documentation

**Files:**
- Modify: `DEVELOPMENT.md`
- Modify: `CLAUDE.md`
- Modify: `README.md` (only if it references the icon — check first)

**Interfaces:**
- Consumes: `tools/generate-icon.py` (Task 2), `AudioLeash/TrayIconLoader.cs` (Task 1).
- Produces: nothing.

- [ ] **Step 1: Check whether README describes the icon**

Run: `grep -rin "icon" README.md`

If it only mentions the tray icon's *behaviour* (right-click, menu), leave it alone — the icon's appearance changed, not its behaviour. If it embeds or describes the icon image, update that reference.

- [ ] **Step 2: Add an icon section to `DEVELOPMENT.md`**

Add under the build/architecture material:

```markdown
## Regenerating the Icon

`AudioLeash/Resources/icon.ico` is generated, not hand-drawn. The source of
truth is `tools/generate-icon.py`.

```bash
python -m pip install Pillow          # dev-time only; not a build dependency
python tools/generate-icon.py           # rewrites AudioLeash/Resources/icon.ico
python tools/generate-icon.py --preview # also dumps PNGs to build/icon-preview/
```

The script renders six frames (16, 32, 48, 64, 128, 256). The 16px and 32px
frames are drawn from **simplified geometry** — a thicker carabiner, no inner
cutout at 16px — rather than downscaled from a large master. A hairline ring
shrunk to 16px dissolves into grey mush, which is what the previous icon did.
`IconAssetTests` enforces this: it fails if the 16px frame ever becomes a plain
downscale of the 32px one.

The tile geometry (corner radius 15.3% of width, vertical gradient, white glyph
at ~54% width) is shared with the AudioStreamer icon so the two apps read as a
family. AudioLeash's gradient is `#22C3DE` → `#0E7C96`.

The generator is deterministic: re-running it on an unchanged script reproduces
the committed `.ico` byte-for-byte.
```

- [ ] **Step 3: Update the project structure tree in `CLAUDE.md`**

In the `## Project Structure` block, add the new entries:

```
├── tools/
│   └── generate-icon.py             ← Regenerates Resources/icon.ico (Pillow, dev-only)
```

and inside `AudioLeash/`:

```
│   ├── TrayIconLoader.cs            ← Loads the tray icon at the requested frame size
```

and inside `AudioLeash.Tests/`:

```
    ├── IconAssetTests.cs
    ├── TrayIconLoaderTests.cs
```

- [ ] **Step 4: Commit**

```bash
git add DEVELOPMENT.md CLAUDE.md
git commit -m "docs: document icon regeneration and new source files"
```

---

## Done When

- [ ] `dotnet test AudioLeash.sln` passes — 59 tests, 0 failed.
- [ ] The tray icon is recognisable at 16px on a real Windows desktop, verified by looking at it.
- [ ] `python tools/generate-icon.py` leaves `git status` clean.
- [ ] `Task 3 Step 4` has been performed: the downscale guard was observed failing on bad artwork.
- [ ] No changes to `AudioLeash.csproj` or `installer/AudioLeash.iss`.
