#!/usr/bin/env python3
"""Renders the WinGetTray app icon (rounded square + download arrow) to src/WinGetTray/app.ico.

The icon is drawn at 8x and downsampled, so the 16 px tray/start sizes stay clean.
Run from the repo root:  python tools/make-icon.py
"""
from PIL import Image, ImageDraw

OUT = "src/WinGetTray/app.ico"
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
BASE = 256
SS = 8  # supersample factor


def rounded_gradient(size: int) -> Image.Image:
    s = size * SS
    grad = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    # vertical gradient in the Windows 11 accent family
    top = (76, 194, 255)     # #4CC2FF
    bottom = (0, 103, 192)   # #0067C0
    strip = Image.new("RGBA", (1, s))
    px = strip.load()
    for y in range(s):
        t = y / max(1, s - 1)
        px[0, y] = (
            round(top[0] + (bottom[0] - top[0]) * t),
            round(top[1] + (bottom[1] - top[1]) * t),
            round(top[2] + (bottom[2] - top[2]) * t),
            255,
        )
    grad = strip.resize((s, s), Image.BILINEAR)

    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.22), fill=255)
    grad.putalpha(mask)
    return grad


def draw_glyph(img: Image.Image) -> Image.Image:
    s = img.size[0]
    d = ImageDraw.Draw(img)
    white = (255, 255, 255, 255)

    stroke = int(s * 0.085)
    cx = s // 2
    shaft_top = int(s * 0.24)
    shaft_bottom = int(s * 0.60)

    # arrow shaft
    d.rounded_rectangle(
        [cx - stroke // 2, shaft_top, cx + stroke // 2, shaft_bottom],
        radius=stroke // 2,
        fill=white,
    )
    # arrow head
    head_half = int(s * 0.17)
    d.polygon(
        [
            (cx - head_half, shaft_bottom - int(s * 0.02)),
            (cx + head_half, shaft_bottom - int(s * 0.02)),
            (cx, shaft_bottom + int(s * 0.14)),
        ],
        fill=white,
    )
    # base line = "install"
    d.rounded_rectangle(
        [int(s * 0.27), int(s * 0.74), int(s * 0.73), int(s * 0.74) + stroke],
        radius=stroke // 2,
        fill=white,
    )
    return img


def main() -> None:
    frames = []
    for size in SIZES:
        big = draw_glyph(rounded_gradient(size))
        frames.append(big.resize((size, size), Image.LANCZOS))

    # PIL writes a multi-resolution .ico when sizes are supplied on save
    largest = max(frames, key=lambda f: f.size[0])
    largest.save(OUT, format="ICO", sizes=[(s, s) for s in SIZES])
    print(f"wrote {OUT} with sizes {SIZES}")


if __name__ == "__main__":
    main()
