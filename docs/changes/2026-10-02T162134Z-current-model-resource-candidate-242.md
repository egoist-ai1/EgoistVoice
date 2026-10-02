# Current-model resource candidate2.4.2

Owner requested further minimum-resource fast/stable current-model work. Root kept current GigaAM pair and CPU4/arena, rejecting headless arena-off/two-thread latency regressions. Removed short-array copy, second native Result read, generic queue result retention and disabled-cue preload. No PCM/model/decode/device/animation setting changes.

Tests: old source fails three new regressions; updated Full1153 pass and Compact1146+7expectedskips. Root verified Windows9/native65file audit manifests; bounded public headless screens/80clip reverse order exact outputs. No personal acoustic or whole-app RAM savings claim. Evidence artifacts/quality-2.4.2/resources/experiments-v1.

Affected source: CaptureOperationQueue.cs; Services/GigaAmTranscriptionService.cs, RussianSpeechQualityService.cs, FeedbackSoundService.cs; version2.4.2; three regression tests. Current status/ticket/release/architecture route updated. Installed/public2.4.1 remains accepted; final native checks/review/CI/package/install/measure/publication pending. Preserve settings/Data/default microphone/pause and immutable prior release. No workstation installer execution.
