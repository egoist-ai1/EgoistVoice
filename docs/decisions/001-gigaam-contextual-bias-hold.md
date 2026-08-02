# Decision 001 — hold GigaAM contextual bias behind the paired corpus gate

- Date: `2026-08-01`
- Status: `HOLD — baseline remains production default`
- Ticket: [`EV-2203`](../tickets/EV-2203-asr-candidate-gate.md)

## Decision

Keep the existing GigaAM v3 RNN-T `modified_beam_search` decoder and conditional
Whisper Large v3 Turbo fallback unchanged in interactive Egoist Voice. Ship no
replacement ASR. Contextual hotwords remain an explicit corpus-candidate mode
until the same private recordings prove every WER/entity/latency negative gate.

## Evidence

- GigaAM ONNX revision remains
  `6888903da215c7735f51101d939f3bfa679fb2b8`; Sherpa-ONNX is `1.13.4`.
- The optional official GigaAM tokenizer is pinned at revision
  `ec1dc1f01d0d627ab2c0d3acc1e235702300d95e`, 255,336 bytes, SHA-256
  `828c12c991019eef952a960661f25a92d6ad279591e2ea466b4aeddf1d20a18a`.
- The minimal SentencePiece reader proves all 1,024 model pieces match the
  installed recognizer token IDs; the sole extra recognizer token is the final
  RNN-T `<blk>` at ID 1,024.
- Native paired smoke initialized both baseline and contextual recognizers,
  accepted hotword catalogue v2 (`400` safe phrases, score `1.15`) and decoded
  500 ms of silence to empty text in both modes.
- Release suite: `453/453` passed. The corpus script parses and the report pins
  decoder mode, hotword version and score.

## Why not activate now

There are no private corpus WAV files or frozen baseline on this machine. A
successful native initialization proves compatibility, not lower WER or zero
false bias. Enabling the candidate globally would violate the approved gate and
could damage ordinary Russian without observable evidence.

## Reopen condition

Run `baseline` and `hotwords` over identical corpus SHA-256. Contextual bias may
supersede this HOLD only if pure-Russian WER regresses by no more than 0.5 pp,
covered entity accuracy improves materially, negative controls remain at zero
false replacements, p95 latency stays within 1.15x and the rerun reproduces the
verdict. A later decision must supersede this file rather than editing it.
