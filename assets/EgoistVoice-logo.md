# Egoist Voice icon

Generated on 2026-09-05 with the built-in `image_gen` tool, mode: new image.

Design brief: a clean, contemporary Windows dictation app logo. A precise red
rounded microphone capsule and U-shaped cradle, with a small negative-space
waveform inside. Red accent around `#F5233D`, clear silhouette at 16–24 px,
balanced padding, no lettering or watermark. One finished mark.

The generated source already contained alpha transparency. No background
removal or generative retouching was applied after generation.

- Master: `EgoistVoice-icon-master.png`, 1254 × 1254 RGBA.
- Master SHA-256: `a2b4f65481c30f2473c9393a5142e0eed52a1269528710e9f9aad219739b1087`.
- WPF image: `EgoistVoice.png`, 256 × 256.
- Windows icon: `EgoistVoice.ico`, native 32-bit DIB frames at 16, 20, 24, 32,
  40, 48, 64, 128 and 256 px.

`scripts/Build-AppIcon.ps1` resizes the source and assembles the ICO using
in-box Windows drawing APIs. Windows `LoadImage` decoded every output size.
Evidence and the previous logo are retained in `artifacts/EV-2223/qa/`.
