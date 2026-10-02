# Literal dictation, streaming capture and Russian RC2

The reported cropped installer page now uses a native frame, explicit busy/error
layouts and native Inno navigation/validation. The capsule is 20% narrower with a
taller, faster wave. Qwen is optional at build time and absent from the default RC2;
the production idle unload timer is removed. Literal mode bypasses lexical changes
and automatic translation/formatting, including when old settings lack the field.

Capture keeps equivalent 16 kHz mono blocks while recording. Exact parity tests
cover seven formats, pre-roll, callback boundaries, EOF, cancellation and restart.
Synthetic retained audio drops 83.5%; whole-process memory is a different quantity.
GigaAM files stream in bounded overlapping portions; the former 30-minute checks
are removed. Existing live float-array contracts retain physical memory limits.

Integration: `6a133d2` (capture), `3430a04` (files), `52d79e4` (UI/settings/package),
`0a6cbc5` (file-editor wording). Main source owners: AudioCaptureService,
GigaAmTranscriptionService, TranscriptPostProcessor/DictationSettingsService,
MainWindow/SettingsWindow, waveform controls, installer scripts and focused tests.

Validation: Full 879; Compact 872+7 Full-only skips; Pester 4 and analyzer clean.
Final package 507 files verified, 253470348 bytes, 2 embedded segments. Native bundled ASR
ordinary/quiet fixtures:8 runs, 0 word errors. Sampled private peak 678 MiB. Four decoder
variants preserve identical outputs, including four repeated-syllable failures on
novel words; no accuracy-tuning change justified. No example-specific replacements.

Installer compilation/package verification passed; lifecycle/busy UI not executed
because no isolated Windows guest is available and host testing is prohibited.
Next: guest lifecycle and comparative real-microphone corpus. No stable release,
installation, publication, model download or unrelated emulator change occurred.
Evidence lives under `artifacts/validation/russian-2.2.1-rc.2`.
