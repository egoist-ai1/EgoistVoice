# EV-2203 — exact GigaAM contextual-bias candidate, production HOLD

- UTC: `2026-08-01T22:24:53Z`
- Ticket: `EV-2203`
- Decision: [`001-gigaam-contextual-bias-hold.md`](../decisions/001-gigaam-contextual-bias-hold.md)

## What and why

Kept GigaAM v3 as Russian primary and the existing Whisper Large v3 Turbo as
conditional mixed-language fallback. Added only the optional metadata required
to test Sherpa contextual bias on that exact stack: a pinned official
SentencePiece model, a standard-library protobuf reader, exact token validation,
deterministic BPE export and versioned safe hotword generation.

Interactive Voice deliberately remains baseline. The corpus CLI now selects
`baseline` or `hotwords` explicitly and records mode, version and score, so an
eventual quality decision cannot compare mismatched configurations.

## Verification

- `dotnet test .\Egoist.Voice.sln --configuration Release --no-restore`:
  `453/453` passed, no warnings or errors.
- `--giga-hotword-smoke`: PASS; 400 phrases; baseline and candidate both
  initialized and returned zero characters for 500 ms silence.
- `run-corpus-baseline.ps1`: PowerShell parser PASS.

## Contracts and files

- Core model readiness still requires the four GigaAM ONNX/token artifacts and
  Whisper; the 255,336-byte tokenizer is optional and integrity-pinned.
- Optional download/token/native failure falls back to baseline and never blocks
  Russian dictation.
- Main files: `Core/ModelCatalog.cs`, `Core/GigaAmHotwordResources.cs`,
  `Services/GigaAmTranscriptionService.cs`, `Services/ModelManager.cs`,
  `Core/CorpusBenchmark.cs`, `App.xaml.cs`, corpus schema/runner/tests and
  Decision 001.

## Residual risk and next step

There are no private WAVs or frozen baseline, so this task claims compatibility,
not improved WER. HOLD can be superseded only by identical-corpus WER/entity/
latency results. EV-2204 proceeds on the baseline decision with conservative
deterministic entities; the private paired gate remains a release blocker.
