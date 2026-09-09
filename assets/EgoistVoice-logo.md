# Scarlet voice mark

Created 2026-09-09 with the built-in image generation tool for the approved flat
black and scarlet capsule. Five rounded speech bars form a central E through
negative space. The second generated master refines spacing, symmetry and contours.

- Master: `EgoistVoice-icon-master.png`.
- WPF image: `EgoistVoice.png`, 256 × 256.
- Windows icon: `EgoistVoice.ico`, 16, 20, 24, 32, 40, 48, 64, 128 and 256 px.
- Installer header: `installer-microphone-52.bmp`, resampled from the same master.
- Build: `python scripts/build-icon.py` resamples the master and verifies all ICO sizes.
- Capsule palette: black `#000000`, scarlet `#FF2448`, text `#FAFAFA`.

Final edit prompt (built-in image generation): refine the supplied five-bar scarlet
waveform logo with its central E-like negative-space notches. Preserve identity,
black background and centered square composition. Make rounded caps, spacing and
outer heights consistent and balanced; request uniform #FF2448 on #000000, crisp
vector-like contours and generous margins. Remove grain, lighting, bevels, gradients
and glow. No text, mockup, border or watermark. Original reference remains in Git.

The generated raster is the source of the icon. Its antialiased edges contain
intermediate colours; the live capsule uses the exact solid palette above.
