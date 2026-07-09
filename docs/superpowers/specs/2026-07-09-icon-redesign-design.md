# AudioLeash Icon Redesign

**Date:** 2026-07-09
**Status:** Approved

## Problem

The current tray icon is illegible at 16x16, the size Windows shows in the
taskbar notification area. Three causes compound:

1. **Sub-pixel strokes.** The waveform and carabiner are drawn with ~2px strokes
   at 256px. Scaled to 16px those strokes land below one pixel and dissolve into
   partial-alpha grey.
2. **No silhouette.** The artwork sits on a transparent background, so the icon
   has no stable outline to anchor recognition. It reads as a smear, not a shape.
3. **Low contrast.** Light cyan (`#5BCFE8`-ish) against a light taskbar has
   little luminance separation.

The carabiner — the element that identifies the app — is the first detail lost.

A fourth cause is in code, not artwork. `GetTrayIcon` calls `new Icon(stream)`,
which selects the `.ico`'s *default* frame (nominally 32x32) rather than the
16x16 frame. `NotifyIcon` then downscales it. Any hand-tuned 16px artwork would
never be displayed. **This premise must be verified empirically before the fix
is claimed to work.** If `new Icon(stream)` turns out to already select 16px,
the loader change is reverted and only the artwork ships.

## Goal

Replace the icon with one that is legible at 16x16 and reads as a sibling of the
AudioStreamer icon (`C:\Users\Andreas\Repos\AudioStreamer\Resources\icon.ico`).

## Family Template

Measured from the AudioStreamer icon's 256px frame, not estimated:

| Property | Value |
|---|---|
| Tile | Fills canvas, rounded rect, on transparency |
| Corner radius | 15.3% of tile width (39px at 256px) |
| Gradient | Vertical, lighter at top |
| AudioStreamer gradient | `#368AFB` (top) -> `#175BCC` (bottom) |
| Glyph colour | Pure white `#FFFFFF` |
| Glyph width | ~54% of tile width |
| Glyph height | ~49% of tile width |

AudioLeash inherits this geometry exactly. Two things change:

- **Gradient:** `#22C3DE` (top) -> `#0E7C96` (bottom). Cyan/teal preserves
  AudioLeash's existing brand colour, deepened for contrast, and stays
  distinguishable from the blue tile when both apps sit in the tray together.
- **Glyph:** the three sound waves are replaced by a carabiner.

## The Glyph

A solid white speaker body on the left, using the same trapezoid-plus-rectangle
construction, size, and position as AudioStreamer's speaker, so the two icons
visibly rhyme.

To its right — occupying the space where AudioStreamer's waves radiate — a
carabiner: a rounded oblong ring with a gate line across it, canted ~20 degrees
so it reads as *hanging* rather than as a letter O. A short white bar links the
speaker to the carabiner: the leash.

## Two Artworks, Not One

This is the core of the fix. No frame is ever produced by shrinking a larger one.

**Full glyph** — 48, 64, 128, 256px:
- Carabiner with inner cutout, visible gate line, full-length link bar.

**Simplified glyph** — 16, 32px:
- Carabiner reduced to a fat closed ring: no gate detail, thicker wall.
- The ring **keeps its inner cutout even at 16px**. This reverses the original
  intent: a solid pill at 16px renders as a featureless vertical bar, which is
  no better than the icon we replaced. A 1px wall still reads as a ring.
  Established by rendering both and looking at them.
- Speaker and ring are separated by a 1px gap at 16px, so they resolve as two
  masses rather than one smear. This gap, not the ring's detail, is what makes
  the icon legible.
- All edges snapped to whole pixels.

Every frame is rendered by supersampling 8x and downsampling. The small frames
are then pixel-snapped.

The shipped `.ico` contains the six sizes the current icon carries: 16, 32, 48,
64, 128, 256. The simplified path covers 16 and 32; the full path covers the
rest. The boundary sits at 48px because that is the smallest size where the
carabiner's inner cutout survives.

## Source of Truth

A committed `tools/generate-icon.py` writes `AudioLeash/Resources/icon.ico` with
all six sizes. Re-running it reproduces the icon deterministically.

Pillow is a **dev-time-only** dependency, invoked manually. It is not added to
any `.csproj` and does not enter the shipped app's dependency graph. The
generator is not wired into the build.

The single `.ico` continues to serve all three consumers, so no `.csproj` and no
`.iss` changes are needed:

- `AudioLeash.csproj`: `<ApplicationIcon>` and `<EmbeddedResource>`
- `installer/AudioLeash.iss`: `SetupIconFile`
- `AudioLeashContext.GetTrayIcon()`: embedded resource stream

## Code Change

`AudioLeash/AudioLeashContext.cs`, in `GetTrayIcon()`:

```csharp
// before
return new Icon(stream);
// after
return new Icon(stream, SystemInformation.SmallIconSize);
```

Gated on empirically confirming that the current call selects a frame larger
than 16x16.

## Verification

**Automated** (`AudioLeash.Tests`, reading the embedded resource):

1. The `.ico` carries all six sizes: 16, 32, 48, 64, 128, 256.
2. The 16px frame resolves as crisp artwork, not dissolved mush, via two
   structural guards: it contains at least 20 near-white pixels (luminance >
   0.95), and its bright pixels (luminance > 0.80) form exactly 2 connected
   masses — the speaker and the carabiner ring. Regenerating with
   `SIMPLIFIED_UP_TO = 0` (all frames from the full hairline geometry) yields 8
   near-white pixels and 5 masses, so both tests fail. An earlier version of
   this guard bilinear-downscaled the 32px frame to 16px and asserted the
   per-pixel difference from the real 16px frame exceeded a resampling-noise
   threshold; that was abandoned because it separated good artwork from bad by
   only 1.34x (5.60 vs 4.17), since both frames are independent LANCZOS
   reductions of a supersampled render and differ substantially either way.
   The structural guard separates by 3.4x.
3. Mean luminance contrast between glyph pixels and tile pixels at 16px clears a
   minimum ratio.

These tests read the `.ico` only and require no Windows audio stack, consistent
with the project's testing constraints. They run on Windows: the test project
targets `net10.0-windows7.0` and the tests decode frames with `System.Drawing`.
The repository has no CI configuration today, so no CI change is needed.

The generator writes PNG-encoded frames (Pillow's ICO default). The tests parse
the `.ico` directory directly and decode each frame's PNG payload, rather than
going through `Icon.ToBitmap()`, which is unreliable for PNG-compressed frames.

**Manual:**

- Render and view every frame, especially 16px.
- Run the app and confirm the tray icon in situ.

No claim about the icon's appearance is made without having looked at the
rendered output.

## Out of Scope

- **Separate light/dark tray variants.** The tile carries its own background, so
  one icon works on both themes. `GetTrayIcon` is theme-agnostic today and stays
  that way.
- **Per-DPI frame selection.** Honouring display scaling (picking the 24px frame
  at 150%) is a larger change; deferred.
- Changing the AudioStreamer icon.

## Documentation

`README.md` and `DEVELOPMENT.md` are updated only if user-facing behaviour or
project structure changes. Adding `tools/generate-icon.py` changes project
structure, so `DEVELOPMENT.md` gets a short note on regenerating the icon, and
`CLAUDE.md`'s structure tree gains the `tools/` entry.
