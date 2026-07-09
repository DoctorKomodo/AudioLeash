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
    "plate": (0.235, 0.415, 0.325, 0.585),
    "cone": ((0.325, 0.415), (0.465, 0.285), (0.465, 0.715), (0.325, 0.585)),
    # The bar is the leash: it must stay visible between cone and ring, so the
    # ring sits far enough right that canting it cannot swallow the bar.
    "bar": (0.465, 0.472, 0.630, 0.528),
    "ring_center": (0.735, 0.500),
    "ring_size": (0.190, 0.320),
    "wall": 0.042,
    "cant": 20.0,
    "gate": True,
    "cutout": True,
}

# Chosen so that at 16px every edge lands on a whole device pixel:
# speaker plate x3..x5, cone to x8, link bar x8..x9, ring x10..x14.
# The 1px gap between bar and ring is what keeps the two masses legible.
SIMPLE = {
    "plate": (0.1875, 0.3750, 0.3125, 0.6250),
    "cone": ((0.3125, 0.3750), (0.5000, 0.2500), (0.5000, 0.7500), (0.3125, 0.6250)),
    "bar": (0.5000, 0.4375, 0.5625, 0.5625),
    "ring_center": (0.7500, 0.5000),
    "ring_size": (0.2500, 0.4375),
    "wall": 0.0625,
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

    # The ring keeps its cutout even at 16px. A solid pill there is
    # indistinguishable from a vertical bar; a 1px wall still reads as a ring.
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
    out.parent.mkdir(parents=True, exist_ok=True)

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
