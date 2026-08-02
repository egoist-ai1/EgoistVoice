# EV-2208 — arbitrate ASR and translation resources

- Status: `PROPOSED — requires ticket approval`
- Specification: [`Voice 2.2`](../specs/001-voice-2.2-accuracy-offline-translation.md)
- Depends on: `EV-2204`, `EV-2206`

## Observable result

Voice completes ASR before heavy translation inference, loads only required
models, releases optional Whisper/MT resources after idle and remains responsive
under repeated dictation/translation without monotonic growth.

## Scope

- Add one internal lifecycle arbiter for capture, GigaAM, optional fallback and
  shared translation requests; UI never blocks on synchronous model work.
- Keep GigaAM ready according to measured startup trade-off; lazy-load optional
  fallback and let the host own MT sleep/wake.
- Cancel superseded work without disposing resources used by another request.
- Measure and bound queues, handles, working set and VRAM; do not add an
  unbounded cache or duplicate model process.
- Handle GPU loss/backend failure by the approved functional fallback and typed
  state, not retry storms.

## Expected files

- `Services/HybridTranscriptionService.cs`
- `Services/WhisperTranscriptionService.cs`, `GigaAmTranscriptionService.cs`
- `Services/TranslatorClient.cs`, `Services/ModelManager.cs`
- likely one internal lifecycle/arbiter service and contract
- `MainWindow.xaml.cs`, `App.xaml.cs`
- hybrid/model/push-to-talk/translator stress tests

## Blockers and human input

- Final ASR strategy and shared host lifecycle contracts.
- Hard figures are evaluated on the recorded release hardware profile.

## Verification

- Pure-RU latency ≤1.15× baseline; warm ≤300-character translation p95 ≤2 s
  and cold-ready p95 ≤15 s on release hardware.
- Host without loaded MT model ≤80 MB private working set.
- 300 trigger cycles, 1,000 translations, cancellation storm, 30-minute active
  and 8-hour idle show bounded working set/handles and one host/model process.
- GPU/backend loss and app shutdown leave no orphan process or hung UI.

## Done when

All resource budgets have raw evidence and the standard Voice + Translate
concurrent workload remains correct and responsive.
