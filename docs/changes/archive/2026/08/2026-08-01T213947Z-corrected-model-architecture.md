# Corrected Voice 2.2 model architecture

- Date (UTC): `2026-08-01T21:39:47Z`
- Scope: specification, ticket dependency order and roadmap only.

## What and why

Recorded the user's corrected direction after `Продолжай`: keep GigaAM v3 as
the Russian primary ASR and the existing Whisper large-v3-turbo as conditional
mixed-language fallback. Replacement ASR candidates and their downloads were
removed from the 2.2 implementation path. EV-2203 now measures sherpa-onnx
contextual hotwords on the exact current Transducer decoder.

The translation gate now compares compact Hy-MT2-1.8B Q6/Q8 candidates. The
missing historical HY-MT1.5 7B file no longer blocks the shared-engine work.
EV-2202 can proceed with deterministic checks while the private corpus remains
a mandatory release/accuracy gate.

## Verification

- Searched the amended spec/tickets/roadmap for the fixed ASR, hotword, Hy-MT2
  and no-new-ASR contracts; every expected contract is present.
- No product source, runtime model, version or installer was changed.

## Affected contracts and risk

- Changes the sequencing and model-selection contract, not runtime behavior.
- Exact GigaAM hotword/tokenizer compatibility and user-voice accuracy remain
  unverified until EV-2202/EV-2203 and the private corpus gate run.

## Next safe action

Implement EV-2202 boundary-safe capture without making an accuracy claim before
the private microphone corpus is recorded.
