# EV-2202 WASAPI memory capture

- Date (UTC): `2026-08-01T21:50:51Z`
- Ticket: `EV-2202`

## What changed

Replaced per-trigger `WaveInEvent` plus live temporary WAV with a continuously
warm shared-mode `WasapiCapture`. The service keeps only a block-aligned 200 ms
idle ring, includes a 350 ms release tail and converts the completed device
format exactly once to 16 kHz mono in memory. GigaAM and conditional Whisper now
accept the same in-memory sample array. Explicit private-corpus recording is the
only mode that persists a completed WAV.

Speech acceptance now estimates the quiet percentile of the pre-roll, applies
bounded noise headroom and recognises sustained or peak-bearing low-energy
consonants. It does not add AGC, denoise, semantic correction or VAD trimming.

## Verification

- `dotnet build .\Egoist.Voice.sln -c Release --nologo`: pass, 0 warnings/errors.
- `dotnet test .\tests\Egoist.Voice.Tests\Egoist.Voice.Tests.csproj -c Release --no-restore --nologo`:
  pass, `411/411`.
- Real `--microphone-smoke`: `PASS`, 12,960 in-memory samples. The room was
  quiet (`hasSpeech=False`), so this proves device/data/lifecycle only.
- `git diff --check`: pass; only existing line-ending notices.

Tests cover ring wrap/alignment/zeroing, pre-roll/tail order, cancellation
isolation, 300 bounded cycles, 48 kHz stereo downmix/resample, adaptive quiet
acceptance and stationary-noise rejection.

## Contracts, risks and next action

Normal dictation no longer creates a recoverable audio path; corpus mode still
does. Device unplug/reconnect backoff, 30-minute endurance and actual quiet/
soft/sibilant/boundary accuracy require the private microphone gate, so no WER
or user-voice improvement is claimed. Next implement EV-2203 contextual bias on
the exact current GigaAM export and keep baseline decoding on incompatibility.
