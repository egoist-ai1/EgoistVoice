from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw


ICON_SIZES = (16, 24, 32, 48, 64, 128, 256)


def build_icon(source: Path, png_output: Path, ico_output: Path, preview_output: Path) -> None:
    image = Image.open(source).convert("RGBA")
    side = min(image.size)
    left = (image.width - side) // 2
    top = (image.height - side) // 2
    image = image.crop((left, top, left + side, top + side))

    # The generated master has a white presentation background outside the tile.
    # Flood-fill only edge-connected white pixels so the central white waveform stays intact.
    for corner in ((0, 0), (image.width - 1, 0), (0, image.height - 1), (image.width - 1, image.height - 1)):
        ImageDraw.floodfill(image, corner, (0, 0, 0, 0), thresh=18)

    master = image.resize((512, 512), Image.Resampling.LANCZOS)
    png_output.parent.mkdir(parents=True, exist_ok=True)
    master.save(png_output, "PNG", optimize=True)
    master.save(ico_output, "ICO", sizes=[(size, size) for size in ICON_SIZES])

    preview = Image.new("RGBA", (420, 112), (28, 28, 30, 255))
    cursor = 16
    for size in (16, 24, 32, 48, 64):
        sample = master.resize((size, size), Image.Resampling.LANCZOS)
        y = (preview.height - size) // 2
        preview.alpha_composite(sample, (cursor, y))
        cursor += size + 24
    preview.save(preview_output, "PNG", optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser(description="Build Egoist Voice PNG/ICO assets")
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--png", type=Path, required=True)
    parser.add_argument("--ico", type=Path, required=True)
    parser.add_argument("--preview", type=Path, required=True)
    args = parser.parse_args()
    build_icon(args.source, args.png, args.ico, args.preview)


if __name__ == "__main__":
    main()
