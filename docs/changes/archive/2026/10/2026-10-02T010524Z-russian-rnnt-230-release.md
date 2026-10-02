# Russian RNNT 2.3.0 — released and installed

The previous E2E profile rejected two reproducible quiet sessions and mixed visible Compact controls with unused models. EV-2230 selects one plain GigaAM v3 RNNT CPU profile, retains all captured PCM, lowers adaptive quiet floors, primes two real native passes and only then signals readiness. Animation scheduling is bounded and avoids unchanged redraws; cancellation remains visible. Four pinned assets total 323,768,215 bytes. INT8/greedy/8 threads were selected from paired public experiments.

Full CI920, Compact913/7, updater12 with rollback/recovery, quiet/capture57 and native UI14/focus8 pass. The final native smoke passes four times. Git checkout's CRLF conversion was reproduced in CI and fixed for exact vocabulary bytes with .gitattributes. Application code was unchanged after UI QA.

Source `2df610ed99c141663dbcdb528397aa6c1b0aa9f2`, merged PR1, [public latest release](https://github.com/egoist-ai1/EgoistVoice/releases/tag/v2.3.0), eight server-digest-verified assets. Exact installed 2.3.0.0/current process readiness and all507 hashes pass after transactional replacement; user preferences/Data preserved. No installer test was run on the workstation. Package review covers PE versions, native runtime and both embedded segments. Receipts are in artifacts/release-qa-2.3.0 and artifacts/upgrade-qa-2026-10-02.

Contracts changed: plain character vocabulary cannot enable historical E2E BPE hotwords and has no intrinsic Latin/punctuation. Capture PCM and delivery contracts remain. Public40/572words yielded16errors vs25, p50/p9591.9/152.4ms, not personal accuracy or complete dictation latency. English names, intonation, clean-VM install/uninstall and historical Full EV-2210 remain limits.

Touched owners: ModelCatalog/RussianAsrProfile, GigaAmTranscriptionService, SpeechActivityDetector, native capsule/settings/tray, installer/updater scripts and meaningful regression tests. Next optional work is a separate personal difficult-speech corpus. No source duplicate was created outside the canonical project; no DNS/brain/other-product change was made.
