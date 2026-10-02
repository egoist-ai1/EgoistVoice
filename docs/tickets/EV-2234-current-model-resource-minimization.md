# EV-2234 — current-model resource minimization

Status: candidate 2.4.2, 2026-10-02. The owner requested further fast/stable current-model optimization with minimum practical resources. Existing build/install/publication authorization continues. Installed/public 2.4.1 remains the accepted baseline; no model replacement is required.

Keep GigaAM v3 plain RNNT INT8 words plus E2E RNNT INT8 punctuation/case, CPU4, the eight pinned weights, PCM/chunk/tail/pre-roll and user preferences. Do not unload warm models or trim working sets to advertise lower RAM. Preserve DNS, private Data/history, the installer-on-workstation boundary and immutable previous releases.

## Implemented and verified

- Reuse an entire original short recording array instead of copying its PCM: a 20-second 16kHz float recording avoids one 1,280,000-byte payload allocation. Slice offsets/counts continue to copy exact bounds. No preprocessing or native decode setting changes.
- Read each native stream Result once, avoiding repeated token/timing marshaling.
- Queue retains a completion-only barrier while the caller owns its typed result/error/cancellation. This fixes generic result retention; ordinary memory dictation already enqueues Cancel in finally, so a persistent ordinary-idle audio leak is not established.
- Disabled sounds do not preload cue players/buffers during startup or settings refresh; explicit preview still creates its cue on demand.

Three deterministic regression cases failed on previous source. Updated Full1153 pass; Compact1146 pass with seven expected Full-only skips. Independent read-only review and exact final-payload native memory/file checks remain before delivery.

## Rejected experiments

Headless paired recognizers on the published 2.4.1 native stack, same pinned public clips: CPU arena disabled saved roughly110MB resident after80 clips but p95 decoding worsened39% and77% in reversed orders. All raw and composed output hashes matched. Two versus four threads was substantially slower with negligible memory reduction. Keep default CPU4/arena. Similar encoder file sizes do not permit sharing weights: all548 verified tensor values differ.

Root verified immutable nine-file Windows and65-file native audit manifests. Experiments use guards, exact models/native pins and public PCM; no user audio or device recording. [Evidence](../../artifacts/quality-2.4.2/resources/experiments-v1/manifest.json).

## Remaining delivery gates

1. Source-bound real-native memory/file parity including long sliced input, no PCM mutation; review any findings.
2. Commit source, Windows CI, clean source-bound self-contained package and independent hash/model/runtime checks.
3. Transactional installed update retaining settings/default microphone/pause/Data, actual installed public-audio CLI and finite idle CPU/RAM/GPU sample.
4. Publish verified 2.4.2 tag/assets, preserve2.4.1, complete measured report and neutral feedback board. No personal accuracy or whole-app RAM savings percentage without measurement.
