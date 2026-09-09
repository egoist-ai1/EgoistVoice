# Egoist Voice — current status

- Updated: 2026-09-09 UTC.
- Active work: **2.2.1-dev**, local scarlet capsule / Russian quality candidate.
- Base: `b91343f` (v2.2.0 source); branch `codex/scarlet-voice-quality`.
- This task did not create a release, tag, installer or publication. Earlier publication
  evidence remains in `docs/releases/` and the immutable change history.

## Current deliverable

`build/Compact-2.2.1-dev/Egoist.Voice.exe`, with four hash-verified local GigaAM models.
Framework-dependent .NET 8 Windows build; separate portable Data. Installed Qwen/runtime
can be reused. The user's previous app and source checkout remain untouched.

Implemented: flat scarlet capsule and new icon; eight measured FFT bands with tapered
edges; quiet boundary preservation; conservative Russian postprocessing and contextual
correction; raw-buffer transfer; no forced process memory trim; safe five-minute Qwen
idle unload; bounded Qwen batches. See [implementation report](IMPLEMENTATION.md).

## Verification and limits

- Full: 818 passed, no skips. Compact: 811 passed, seven Full-only download/pruning
  contracts explicitly skipped; those seven pass in Full.
- Native Compact build: zero warnings/errors. WPF state/settings previews inspected.
- Normal and −18 dB synthetic speech: zero word errors in four runs each; copied
  models exercised offline from the delivered build directory.
- Native Qwen: 12/13 synthetic fixtures pass. One spelling response is rejected and
  the original retained. General acoustic/semantic accuracy is not established.
- No installer lifecycle, real microphone corpus or hardware/endurance claim is made.

## Next action

Exercise this local candidate on real microphone speech. Review remaining Qwen
correction failure and complete corpus/hardware/installer gates before any release.

[Build](docs/BUILD.md) · [User guide](docs/USER-GUIDE.md) · [Changes](docs/changes/INDEX.md)
