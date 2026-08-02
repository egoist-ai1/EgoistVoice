# EV-2202 — boundary-safe WASAPI capture

- Status: `IMPLEMENTED — deterministic and real-device smoke pass; private
  corpus/endurance release gates pending`
- Specification: [`Voice 2.2`](../specs/001-voice-2.2-accuracy-offline-translation.md)
- Depends on: approved corrected architecture; EV-2201 human recordings remain
  required for the release claim, not for deterministic implementation/tests.

## Observable result

The existing hotkey/mouse workflow retains speech already present immediately
before trigger activation and shortly after release. Quiet meaningful speech is
accepted relative to measured room noise, while silence is still rejected.

## Scope

- Replace per-press `WaveInEvent` ownership with shared-mode WASAPI capture,
  a bounded pooled ring, 200 ms initial pre-roll and 350 ms initial tail.
- Capture the device mix format, resample once to ASR format and expose bounded
  pause metadata for later formatting.
- Reconnect on device change/removal with backoff and typed UI state; never spin
  or silently switch to an unexpected microphone.
- Keep audio memory-only, zero/release buffers on shutdown and preserve Esc,
  short/silent rejection and existing delivery safety.
- Adaptive gate estimates pre-roll noise floor but never trims accepted audio;
  AGC/denoise/VAD-cut remain disabled unless separately measured.

## Expected files

- `Services/AudioCaptureService.cs`, `Services/PushToTalkCoordinator.cs`
- `Services/Contracts.cs`, `Services/DictationSettingsService.cs`
- likely new internal ring/resampler types under `Core/` or `Services/`
- `MainWindow.xaml.cs`
- `tests/Egoist.Voice.Tests/AudioCaptureServiceTests.cs`
- `tests/Egoist.Voice.Tests/AudioLevelTests.cs`, `PushToTalkTests.cs`

## Blockers and human input

- Frozen EV-2201 boundary/quiet samples and one real-microphone pass are ship
  gates. Until supplied, implementation may pass deterministic/synthetic gates
  but must not claim measured user-voice improvement.
- An exclusive-mode device conflict is reported; it does not authorize an
  unmeasured MME fallback.

## Verification

- Synthetic ring/property tests prove ordering, wraparound, exact bounds,
  cancellation and no stale audio between sessions.
- Gate corpus has 0 clipped target beginnings/endings, at least 95% accepted
  quiet sessions and at least 15% relative quiet-subset WER improvement.
- Trigger → listening state p95 ≤100 ms; capture callback performs no disk I/O
  or inference and shows bounded allocation/CPU behavior.
- 300 press/release/cancel cycles, device remove/re-add and 30-minute capture
  show no monotonic handle or memory growth.

## Done when

Current insertion behavior works through the new capture path and every
boundary/resource threshold passes without storing user audio.

## Implementation record — 2026-08-02

- Replaced per-press `WaveInEvent`/live `WaveFileWriter` with one continuously
  warm shared-mode `WasapiCapture`, a 200 ms block-aligned ring and 350 ms tail.
- Normal dictation is memory-only. Device-format PCM/float is downmixed and WDL
  resampled once after capture; both GigaAM and conditional Whisper now accept
  the same final sample array. Explicit corpus mode alone writes a completed
  16 kHz WAV.
- Adaptive speech acceptance derives a quiet-percentile floor from pre-roll,
  uses a conservative noise headroom and a peak-aware low-energy-consonant path;
  no AGC, denoise or VAD trimming was introduced.
- Unit coverage proves ring wrap/alignment/clear, pre-roll + tail ordering,
  cancellation isolation, 300 bounded sessions, 48 kHz stereo conversion,
  quiet acceptance and stationary-noise rejection.

Fresh evidence: Release suite `411/411` passed; actual default-device WASAPI
smoke returned `PASS` with `12,960` in-memory 16 kHz samples and no normal WAV.
The machine was quiet during the smoke, so `hasSpeech=False` is expected and is
not an accuracy measurement.

Still required before closing the ticket as release evidence: private quiet/
boundary recordings, 30-minute endurance, device remove/re-add and measured
trigger/latency/resource thresholds. No user-voice improvement is claimed yet.
