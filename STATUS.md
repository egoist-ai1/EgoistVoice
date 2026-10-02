# Egoist Voice — current status

Updated: 2026-10-02. **EV-2230 / 2.3.0 Russian RNNT: published and installed.**

[Public latest release](https://github.com/egoist-ai1/EgoistVoice/releases/tag/v2.3.0), eight assets verified by GitHub sizes/digests. Tag v2.3.0 points to clean source `2df610ed99c141663dbcdb528397aa6c1b0aa9f2`; PR #1 merged. Offline setup: 250,611,668 bytes, SHA-256 `16a9b3712542c2d5b37be4b8d405b890765e2d318f23a5a5edbddd06ebc33aee`.

Installed Compact replaced 2.2.1-rc.3 with 2.3.0.0; all 507 payload hashes verified. Exact current process/path and real RNNT readiness confirmed; theme, microphone, history and notification preferences preserved. One literal Russian CPU profile, Qwen/mixed mode disabled. Receipts: `artifacts/release-qa-2.3.0`.

CI Full 920/920; Compact 913 passed / 7 Full-only skips; updater 12/12 with actual isolated replacement, rollback and interrupted recovery. Quiet gate reproduced two failures before the fix; 57 capture/gate tests pass after. Native UI 14/14 and focus checks 8/8; only .gitattributes changed afterwards. Final native ASR smoke: four runs, zero errors in 26 public reference words each. Independent final package review passes all bytes/models/versions/segments.

Public paired pilot: 40 clips / 572 words, plain INT8 greedy 16 errors vs E2E 25; warm decode p50/p95 91.9/152.4 ms. Personal voice accuracy, English spelling, intonation and clean-Windows installer lifecycle remain unverified. Unsigned installer; no zero-error claim. Historical Full EV-2210 is separate.

Next: collect optional personal difficult-speech examples for a separate measured follow-up. Current release needs no further approval. [Release details](docs/releases/2.3.0.md); [EV-2230](docs/tickets/EV-2230-russian-rnnt.md).
