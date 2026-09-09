# Egoist Voice — current status

- Updated: 2026-09-09 UTC.
- Candidate: **2.2.1-rc.2**, offline Russian installer and portable app.
- Branch: `codex/scarlet-voice-quality`; payload source: `0a6cbc5`.
- Local packaging completed. No tag, push, publication or host installation.

## Current deliverable

`artifacts/Russian-2.2.1-rc.2-final/EgoistVoice-Setup-Russian-2.2.1-rc.2-win-x64.exe`
is one 253,470,348-byte package. Includes Compact/GigaAM and .NET 8.0.30.
Qwen is omitted by default; older installed assets are preserved.

The capsule is 256 × 48 DIP, 20% narrower, with a taller and more responsive wave.
Literal mode preserves decoder text without dictionary, commands, translation or Qwen.
Production has no five-minute model unload. Capture retains equivalent 16 kHz mono
blocks; GigaAM files stream without the former 30-minute cap. Installer pages use a
native movable frame and explicit busy/failure layouts.

## Verification and limits

- Full 879 passed; Compact 872 passed / 7 Full-only skips; builder 4 passed, analyzer clean.
- Capture: bit-for-bit old/new parity across seven formats. Synthetic minute retains
  3.80 MB instead of 23.04 MB raw (83.5% less, not whole-process RAM).
- Final ASR: ordinary and −18 dB fixture, four runs each, zero word errors.
  Sampled ASR peak 678 MiB private memory; no claim of 70% whole-app reduction.
- Four decoder variants all lose a final repeated syllable on four novel-word fixtures.
  No unsupported accuracy claim or shipping decoder change.
- All 507 staged hashes and both embedded installer segments verified.
- Unsigned RC. Clean Windows lifecycle/busy UI unverified: no guest available and
  host installation prohibited. Native app previews passed visual review.
- Live recording grows with duration (~230 MB/hour), with a second array at Stop.
  Physical memory/disk limits remain.

## Next action

Run installer lifecycle in an isolated Windows guest and compare real microphone
speech across ASR candidates before a stable release. See [implementation](IMPLEMENTATION.md)
and [RC notes](docs/releases/2.2.1-rc.2.md).
