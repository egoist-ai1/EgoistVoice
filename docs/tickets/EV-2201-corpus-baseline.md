# EV-2201 — freeze the corpus and 2.1.1 baseline

- Status: `IN PROGRESS — harness verified; awaiting private user recording`
- Specification: [`Voice 2.2`](../specs/001-voice-2.2-accuracy-offline-translation.md)
- Depends on: approved specification

## Observable result

One command records or evaluates the approved corpus sets and emits a
machine-readable baseline report for the untouched dirty 2.1.1 candidate:
per-set WER, entity accuracy, split count, command precision/recall,
punctuation signals, latency and process resources. The report contains no
audio or recognized text.

## Scope

- Extend the existing corpus script/recorder for quiet/sibilant/soft sounds,
  first/last phonemes, `Claude Code`/`Anthropic` and broader RU↔EN entities,
  split words, translation-command positives/negatives and long-form pauses.
- Version the corpus schema and reject missing/duplicate IDs, wrong set counts,
  leaked absolute paths and unlabelled private data.
- Keep recordings/reference text outside Git and logs; source-control only the
  redacted script, schemas and aggregated results.
- Capture hardware, app/model hashes and exact parameters so all later A/B runs
  use the same inputs.

## Expected files

- `Core/CorpusScript.cs`, `Core/CorpusBenchmark.cs`, `App.CorpusRecorder.cs`
- `tests/corpus/script.jsonl`, `tests/corpus/README.md`
- `tests/Egoist.Voice.Tests/CorpusScriptTests.cs`
- `tests/Egoist.Voice.Tests/CorpusGateTests.cs`
- a bounded benchmark script/report schema under `scripts/` and `artifacts/`

## Blockers and human input

- The user records all 350 private clips through `scripts/run-corpus-baseline.ps1 -Record`.
- No product-code ticket starts until the same corpus has a complete baseline.

## Implementation evidence

- Corpus schema v2 locks 12 sets / 350 clips, 109 entity expectations and 40/80
  translation-command positive/negative cases.
- The runner records and evaluates with one command, refuses implicit model downloads and
  atomically protects the frozen baseline from accidental overwrite.
- The aggregate report captures hashes, hardware, exact pipeline parameters, WER/CER, entities,
  split words, commands, punctuation, boundaries, latency and resources without serializing
  audio, transcripts, exception text or absolute paths.
- Fresh automated verification: 402/402 tests; 363/363 shipped JSONL nodes match the v2 schema;
  Release build and Windows PowerShell parser pass.
- Current private state remains intentionally empty: 0 WAV, no `reference.jsonl`, no baseline.

## Verification

- Schema/unit tests and a redacted fixture run cover every set and metric.
- Canary recognized text is absent from app log, benchmark log and Git status.
- Two runs over the same fixture produce byte-stable JSON apart from explicit
  timestamp/runtime fields.
- Baseline report identifies 2.1.1 source/model/hardware and does not claim an
  accuracy improvement.

## Done when

The private corpus is complete, frozen and benchmarkable; baseline evidence is
linked from `STATUS.md`. Missing corpus is a blocker, not a waived assertion.
