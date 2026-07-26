from __future__ import annotations

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "assets"
BACKGROUND = (2, 2, 3, 255)


def render_icon(source_name: str, output_name: str, size: int, padding: int) -> None:
    source = Image.open(ASSETS / source_name).convert("RGBA")
    alpha_box = source.getchannel("A").getbbox()
    if alpha_box is None:
        raise ValueError(f"Generated icon has no visible pixels: {source_name}")

    icon = source.crop(alpha_box)
    target = size - (padding * 2)
    icon.thumbnail((target, target), Image.Resampling.LANCZOS)

    canvas = Image.new("RGBA", (size, size), BACKGROUND)
    left = (size - icon.width) // 2
    top = (size - icon.height) // 2
    canvas.alpha_composite(icon, (left, top))
    canvas.convert("RGB").save(ASSETS / output_name, format="BMP")


def main() -> None:
    render_icon("installer-microphone-v2.png", "installer-microphone-52.bmp", 52, 3)
    render_icon("installer-text.png", "installer-text-26.bmp", 26, 2)
    render_icon("installer-privacy.png", "installer-privacy-26.bmp", 26, 2)


if __name__ == "__main__":
    main()
