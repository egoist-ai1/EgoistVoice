# Scarlet voice mark

Created 2026-09-09 with the built-in image generation tool for the approved flat
black and scarlet capsule. Five rounded speech bars form a central E through
negative space. The master is retained without creative retouching.

- Master: `EgoistVoice-icon-master.png`.
- WPF image: `EgoistVoice.png`, 256 × 256.
- Windows icon: `EgoistVoice.ico`, 16, 20, 24, 32, 40, 48, 64, 128 and 256 px.
- Build: `python scripts/build-icon.py` resamples the master and verifies all ICO sizes.
- Capsule palette: black `#000000`, scarlet `#FF2448`, text `#FAFAFA`.

Generation prompt: a final Windows dictation app icon, a bold scarlet waveform
with a subtle E monogram on pure black; five thick rounded bars with asymmetric
rhythm, balanced negative space, clear at 24 px; flat 2D, no gradients, glass,
shadows, outlines, metallic surfaces, extra dots, words or watermark.

The generated raster is the source of the icon. Its antialiased edges contain
intermediate colours; the live capsule uses the exact solid palette above.
