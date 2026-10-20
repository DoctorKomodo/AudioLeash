#!/usr/bin/env python3
"""Regenerate AudioLeash/Resources/icon.ico.

The icon is a rounded-square cyan tile carrying a white speaker, a short
"leash" bar and an open carabiner hook, matching the AudioStreamer icon's tile
geometry so the two apps read as siblings. The tile fills the whole canvas and
the glyph is sized large so it stays legible in the tray.

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
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]  # 20/24/40: tray at 125/150/250% DPI
SIMPLIFIED_UP_TO = 32        # sizes <= this use the SIMPLE geometry

GRADIENT_TOP = (0x22, 0xC3, 0xDE)
GRADIENT_BOTTOM = (0x0E, 0x7C, 0x96)
CORNER_RADIUS_FRAC = 0.153   # measured from the AudioStreamer icon

WHITE = (255, 255, 255, 255)
CLEAR = (255, 255, 255, 0)

# --- Large-frame geometry ---------------------------------------------------
# Traced from the reference artwork (a 975px-wide tile). Coordinates below are
# reference pixels; _ref() converts them to fractions of the tile width.
_REF_X0, _REF_CY, _REF_W = 281.0, 752.0, 975.0


def _ref(x: float, y: float) -> tuple[float, float]:
    return (x - _REF_X0) / _REF_W, (y - _REF_CY) / _REF_W + 0.5


def _refx(x: float) -> float:
    return _ref(x, 0)[0]


def _refy(y: float) -> float:
    return _ref(0, y)[1]


FULL = {
    # speaker: narrow plate, then a cone widening to a vertical front edge
    "plate": (_refx(419), _refy(660), _refx(528), _refy(845)),
    "cone": (_ref(528, 660), _ref(683, 535), _ref(683, 970), _ref(528, 845)),
    # the leash: a bar, rounded at the carabiner end, that enters the hook's gate
    "bar": (_refx(709), _refy(718), _refx(940), _refy(790)),
    "bar_radius": 0.018,
    # carabiner: stroked centreline of an open hook, opening to the left
    "hook": [_ref(*pt) for pt in (
        (868, 720), (866, 650), (890, 596), (945, 574), (1010, 585),
        (1065, 625), (1081, 678), (1070, 728), (1050, 820), (1039, 870),
        (1020, 915), (965, 926), (910, 905), (885, 860), (884, 790),
    )],
    "hook_width": 72 / _REF_W,
    # flat cuts of the two arm ends (the gate the bar passes through)
    "gate_y": (_refy(691), _refy(818)),
    "gate_x_max": _refx(955),
}

# SIMPLE is laid out on the 16px grid (fractions of 1/16) so every edge lands
# on a whole device pixel: plate x2..4, cone to x7, bar x8..11, hook x10..14.
# The 1px gap between cone and bar, and between bar and hook arms, keeps the
# masses legible. The hook is a 1px-wall square ring with its left wall cut open.
SIMPLE = {
    "plate": (0.125, 0.375, 0.25, 0.625),
    "cone": ((0.25, 0.375), (0.4375, 0.25), (0.4375, 0.75), (0.25, 0.625)),
    "bar": (0.5, 0.4375, 0.6875, 0.5625),
    "ring": (0.625, 0.25, 0.875, 0.75),
    "wall": 0.0625,
    "gate_y": (0.375, 0.625),
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


def _catmull_rom(points: list[tuple[float, float]], steps: int = 24):
    """Smooth path through `points` (endpoints duplicated)."""
    pts = [points[0], *points, points[-1]]
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = pts[i - 1], pts[i], pts[i + 1], pts[i + 2]
        for k in range(steps):
            t = k / steps
            yield tuple(
                0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t
                       + (-a + 3 * b - 3 * c + d) * t ** 3)
                for a, b, c, d in zip(p0, p1, p2, p3)
            )
    yield points[-1]


def _glyph_full(n: int, spec: dict) -> Image.Image:
    layer = Image.new("RGBA", (n, n), CLEAR)
    draw = ImageDraw.Draw(layer)

    draw.rectangle([v * n for v in spec["plate"]], fill=WHITE)
    draw.polygon([(x * n, y * n) for x, y in spec["cone"]], fill=WHITE)

    # Hook: stamp discs along the smoothed centreline, then slice the two arm
    # ends flat so the opening reads as a gate.
    r = spec["hook_width"] * n / 2
    for x, y in _catmull_rom(spec["hook"]):
        draw.ellipse([x * n - r, y * n - r, x * n + r, y * n + r], fill=WHITE)
    gate_top, gate_bottom = spec["gate_y"]
    draw.rectangle(
        [0, gate_top * n, spec["gate_x_max"] * n, gate_bottom * n], fill=CLEAR
    )
    # Cone and plate sit left of the cut, so redraw nothing: the cut only
    # reaches x_max, which is right of the speaker, so restore the speaker.
    draw.rectangle([v * n for v in spec["plate"]], fill=WHITE)
    draw.polygon([(x * n, y * n) for x, y in spec["cone"]], fill=WHITE)

    x0, y0, x1, y1 = (v * n for v in spec["bar"])
    draw.rounded_rectangle([x0, y0, x1, y1], radius=spec["bar_radius"] * n, fill=WHITE)
    draw.rectangle([x0, y0, (x0 + x1) / 2, y1], fill=WHITE)  # square left end
    return layer


def _glyph_simple(n: int, spec: dict, snap: int) -> Image.Image:
    """Pixel-grid geometry; `snap` rounds coordinates to device pixels."""

    def p(fraction: float) -> float:
        value = fraction * n
        return round(value / snap) * snap

    layer = Image.new("RGBA", (n, n), CLEAR)
    draw = ImageDraw.Draw(layer)
    draw.rectangle([p(v) for v in spec["plate"]], fill=WHITE)
    draw.polygon([(p(x), p(y)) for x, y in spec["cone"]], fill=WHITE)

    x0, y0, x1, y1 = (p(v) for v in spec["ring"])
    wall = p(spec["wall"])
    # Square corners: at 16px a rounded 1px wall breaks into diagonal-only
    # fragments and the hook stops reading as one shape.
    draw.rectangle([x0, y0, x1 - 1, y1 - 1], fill=WHITE)
    draw.rectangle([x0 + wall, y0 + wall, x1 - wall - 1, y1 - wall - 1], fill=CLEAR)
    gate_top, gate_bottom = spec["gate_y"]
    draw.rectangle([x0 - 1, p(gate_top), x0 + wall - 1, p(gate_bottom) - 1], fill=CLEAR)

    draw.rectangle([p(v) for v in spec["bar"]], fill=WHITE)
    return layer


def render(size: int) -> Image.Image:
    """One fully-composited frame at `size` x `size`."""
    n = size * SS
    if size <= SIMPLIFIED_UP_TO:
        glyph = _glyph_simple(n, SIMPLE, SS)
    else:
        glyph = _glyph_full(n, FULL)
    composed = Image.alpha_composite(_tile(n), glyph)
    return composed.resize((size, size), Image.LANCZOS)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--preview", action="store_true", help="also dump PNGs")
    parser.add_argument("--out", type=Path, default=None)
    args = parser.parse_args()

    repo = Path(__file__).resolve().parent.parent
    out = args.out or repo / "AudioLeash" / "Resources" / "icon.ico"
    out.parent.mkdir(parents=True, exist_ok=True)

    frames = {size: render(size) for size in SIZES}

    # Pillow's ICO writer matches append_images by exact size and embeds them
    # verbatim; only sizes with no matching image get thumbnail()-downscaled.
    # Supplying every size means none is ever downscaled.
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
