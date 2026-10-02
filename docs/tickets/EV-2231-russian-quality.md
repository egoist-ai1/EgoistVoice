# EV-2231 — Russian dictation quality and formatting

Status: active, 2026-10-02. User feedback on installed 2.3.0: difficult Russian speech still misrecognized; no automatic punctuation or intonation-aware formatting. English names are secondary but required. The owner requests implementation, measured tests, rebuild, publication and workstation replacement.

Latest resource scope: large local models and GPU allowed; the previous hard +20–25% budget has been relaxed. Approximate 50% improvement is a test target, not an accuracy, speed or resource guarantee. Report lexical WER, punctuation/case, English-name exact match, latency and resource footprint separately. Do not imply that public reading accuracy establishes personal speech quality or prosody.

Compare the installed plain GigaAM RNNT against Russian fine-tuned Whisper large-v3, Russian Turbo Stage AW, Qwen3-ASR1.7B and cheap formatting baselines. Pin artifacts and licenses, preserve all original capture PCM, test silence/quiet speech and repeated cancellation. GPU measurements of this team run sequentially; other user GPU workloads are observed and labeled, never stopped.

Accepted benchmark inputs and evidence: `artifacts/quality-2.3.1`. Public book evaluation has 40 predetermined clips, 572 words, 287.09 seconds. It has no Latin names and contains literary punctuation/case. Add an independent conversational sample before interpreting quality gains. Personal recordings and hypotheses stay local; no user audio/text in GitHub, logs, release or cloud inference.

Choose one user-facing optimized quality profile after actual comparisons. Integrate punctuation/casing, predictable readiness and recovery, bounded idle use, existing safe delivery/privacy behavior and smooth capsule. Verify source-bound payload and transactional local replacement. Existing 2.3.0 release/tag remain immutable; DNS and unrelated projects remain out of scope.
## Accepted implementation — source candidate

Stock plainINT8 + E2EINT8 audio formatting selected after paired80 tests; corrected native and larger GPU candidates not promoted. Actual service reproduces lexical26/1169, punctuation130 and rawchar241 with protected words and known names. Full1037, Compact1030/7skip and native cancellation/recovery/silence/Unicode/longaudio pass. Packaging, independent review, publication and installed readback remain pending; see STATUS and Evaluation2.4.
