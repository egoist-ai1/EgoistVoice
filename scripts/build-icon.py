#!/usr/bin/env python3
"""Build Windows icon sizes from the checked-in scarlet logo master.

The master is the original generated artwork. This script only resamples and
encodes that asset; running it never replaces the design with another drawing.
"""

from pathlib import Path
from PIL import Image

ASSETS = Path(__file__).resolve().parent.parent / "assets"
SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def main():
    with Image.open(ASSETS / "EgoistVoice-icon-master.png") as source:
        master = source.convert("RGBA")
    if master.width != master.height or master.width < 256:
        raise ValueError("The logo master must be square and at least 256 pixels.")
    master.resize((256, 256), Image.Resampling.LANCZOS).save(ASSETS / "EgoistVoice.png")
    master.save(ASSETS / "EgoistVoice.ico", format="ICO", sizes=[(size, size) for size in SIZES])
    with Image.open(ASSETS / "EgoistVoice.ico") as icon:
        if icon.ico.sizes() != {(size, size) for size in SIZES}:
            raise ValueError("The Windows icon is missing a required size.")
    print("Built EgoistVoice.png and EgoistVoice.ico (16–256 px).")


if __name__ == "__main__":
    main()
