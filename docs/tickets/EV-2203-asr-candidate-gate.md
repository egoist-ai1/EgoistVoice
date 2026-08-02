# EV-2203 — contextual bias on the fixed ASR path

- Status: `IMPLEMENTED — HOLD baseline; paired private-corpus gate pending`
- Specification: [`Voice 2.2`](../specs/001-voice-2.2-accuracy-offline-translation.md)
- Depends on: `EV-2202`; no ASR model download

## Observable result

Current GigaAM v3 remains RU primary and current Whisper remains the conditional
mixed-language fallback. A reproducible gate decides only whether sherpa
hotwords improve covered entities without harming ordinary Russian speech.

## Scope

- Run the same corpus through current decoding with and without bounded
  contextual hotwords on the exact installed GigaAM export/tokenizer.
- Record exact model hash, sherpa runtime and decoding/hotword parameters.
- Measure RU WER, mixed WER, entity exactness, split errors, latency, RAM/VRAM,
  cold/warm behavior and failure fallback on the same hardware.
- Keep hotword generation deterministic, Cyrillic-tokenizer compatible and
  derived only from versioned catalog/profile aliases.
- Produce a decision note that activates hotwords or records `keep baseline`;
  either result feeds EV-2204 without adding another ASR runtime.

## Expected files

- `Core/CorpusBenchmark.cs`, `Core/RecognitionScorer.cs`
- hotword catalog/file builder and benchmark support under `Core/`,
  `tests/corpus/` and `scripts/`
- focused test fixtures under `tests/Egoist.Voice.Tests/`
- one immutable decision under `docs/decisions/`
- no production default/model manifest change in this ticket

## Blockers and human input

- Exact-model compatibility must be proven. If sherpa rejects the hotword file
  or entity/negative controls regress, production keeps baseline decoding.

## Implementation evidence

- Official pinned SentencePiece metadata is optional and does not enter core
  model readiness; missing/corrupt/mismatched resources fall back to baseline.
- Current deterministic catalogue emits a matching `bpe.vocab` and 403 safe phrases
  at score `1.15`; ambiguous profile-only aliases are excluded globally.
- `--corpus-benchmark ... <label> baseline|hotwords` records the exact mode,
  catalogue version and score. The runner keeps model downloads disabled.
- Native paired smoke passed for baseline and contextual recognizers, with
  empty output for silence in both modes; Release tests pass `453/453`.
- Decision: [`001-gigaam-contextual-bias-hold.md`](../decisions/001-gigaam-contextual-bias-hold.md).
  Compatibility is proven, but no user WAV/baseline exists, so interactive
  Voice intentionally remains baseline until the paired quality gate.

## Verification

- Runner produces complete per-engine metrics from identical ordered samples;
  missing/failed samples cannot disappear from averages.
- Pure-RU WER may not regress by more than 0.5 pp; candidate must materially
  improve mixed/entity sets and fit the approved resource envelope.
- A rerun with fixed inputs reproduces the verdict within declared tolerance.
- The decision is `keep baseline` when no candidate satisfies every hard gate.

## Done when

EV-2204 receives a measured contextual-bias policy; GigaAM and Whisper artifact
identity are unchanged and there is no model replacement/download path.
