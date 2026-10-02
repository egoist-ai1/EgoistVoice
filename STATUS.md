# Egoist Voice — current status

Updated: 2026-10-02. **EV-2234 current-model resource minimization is active; candidate2.4.2 is tested locally.** Installed/public2.4.1 remains the accepted baseline. [Active ticket](docs/tickets/EV-2234-current-model-resource-minimization.md), [candidate release](docs/releases/2.4.2.md), [previous measured resources](docs/models/EVALUATION-2.4.1.md).

Keep local GigaAM v3 plain RNNT INT8 words + E2E RNNT INT8 audio punctuation/case, sequential greedy CPU4 per engine and all eight unchanged pinned models650090519B. No acoustic/model/DSP/chunk/tail/pre-roll changes.

Source fixes: short full-array PCM reuse (20s avoids one1.28MB payload copy), one native Result read, completion-only capture queue barrier, no disabled-cue preloading. Generic queue retention fixed; ordinary memory dictation finally Cancel already releases its result, so an ordinary-idle audio leak is not established. Three regression cases failed before changes; Full1153 and Compact1146+7 expected Full-only skips pass.

Measured/rejected headless native alternatives: arena-off saved~110MB resident after80 clips but p95 worsened39%/77% in both orders; all raw/composed outputs equal. Two threads much slower for negligible RAM savings. Preserve CPU4/arena/warm models. Root verified nine-file Windows and65-file native read-only audit manifests. [Frozen experiment evidence](artifacts/quality-2.4.2/resources/experiments-v1/manifest.json).

Next gates: independent review; final-payload real-native memory/file and long-slice parity; Windows CI; source-bound package; transactional installation; actual installed CPU/RAM/GPU measurements; publish and report2.4.2 using existing explicit authorization. No new personal acoustic or whole-app RAM savings percentage established.

Previous2.4.1/source66f79ad release assets, accepted evidence and installed data are immutable. Its actual finite44.51s idle sample: whole-machine16CPUavg0.008775%,WS917.5MB/private842.8MB,GPUmaxownedengine0%,ded12.31MB. Its cold public11.35s CLI median2.371s includes model load/exit, not warm dictation. [Published baseline](https://github.com/egoist-ai1/EgoistVoice/releases/tag/v2.4.1).

Preserve preferences/default microphone/manual pause, private Data/history, DNS and unrelated projects. Installer is never executed on the workstation. Historical Full gates, personal acoustic5–10% gain, real-driver endurance and isolated installer lifecycle remain unproven; accepted cleanup/model quality/UI details stay linked from prior2.4.1 reports.
