# Scarlet capsule, Russian correction and bounded runtime

Local development candidate `2.2.1-dev`, based on `b91343f` in `codex/scarlet-voice-quality`.
Requested result: implement the selected flat scarlet capsule and new logo, improve quiet
Russian dictation and contextual spelling, and reduce unnecessary memory/CPU use.

The capsule now uses eight measured FFT bands, fifteen tapered bars and asymmetric
attack/release. New icon assets are derived from the checked-in generated master.
Capture retains quiet boundaries, transfers raw buffer ownership into conversion,
and postpones the stop sound until recording has ended. Russian postprocessing keeps
ambiguous expressions and punctuation. Automatic Qwen formatting preserves wording;
manual correction remains conservative and reviewable. Owned Qwen leases and a
monotonic idle deadline unload the model safely; 512/256 batch limits lower peak memory.

Validation: Full 818 passed; Compact 811 passed with seven explicitly Full-only
model-download/pruning cases skipped (all seven pass in Full). Native Compact builds
without warnings. Normal and −18 dB synthetic speech each return zero word errors over
four runs; delivered local models are hash-verified and independently exercised offline.
Qwen native quality check passes 12/13 cases: one spelling response is rejected while
the original is preserved. This is an open limitation, not a green quality gate.
Native WPF recording, processing, success, error and settings previews were inspected.

Contracts: no model downloads, installer execution, remote publication, private corpus
collection or replacement of the running app. No recognized text/audio logging added.
Changes primarily touch Controls/CapsuleWaveform*, MainWindow*, AudioCaptureService,
VoiceSpectrumAnalyzer, TranscriptPostProcessor, RussianPunctuationRestorer,
LocalTextFormatter, LocalQwenHost, HybridTranscriptionService, SettingsWindow, assets,
tests and relevant architecture/user documentation. Build output is ignored by Git.

Measurements, exact launch path and limits: [implementation report](../../IMPLEMENTATION.md).
Next step: user exercises the candidate on real microphone speech. Larger corpus,
hardware/endurance and installer lifecycle gates remain outstanding before release.
