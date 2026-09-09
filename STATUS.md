# Egoist Voice — current status

- Updated: 2026-09-09 UTC.
- Candidate: **2.2.1-rc.3**, branded React installer.
- Branch: `codex/scarlet-voice-quality`; shell/build source `201ed8c`, Voice payload `997e134`.
- Local build only. No publication or host installation.

## Deliverable

`artifacts/React-2.2.1-rc.3-final/EgoistVoice-Setup-Russian-2.2.1-rc.3-win-x64.exe`

React draws the black/scarlet installer, visible text, toggles, folder selection,
real progress and result states. An isolated Electron shell invokes the existing
Inno engine silently; Electron is not part of installed Voice. GigaAM and .NET are
included offline; Qwen is excluded.

Literal mode now fixes only confirmed Egoist product-name aliases and the missing
letter in `репозиторй`. Other text, unknown words and paths remain unchanged.
RC2's narrow capsule, equivalent streaming capture and duration-cap removal remain.

## Verification and limits

- Final focused literal/spelling tests: 23 passed. No broad repeated ASR suite.
- Native application publish, Inno compile, React build and Electron packaging.
- Four UI states, checkbox interaction and mocked install transition checked in
  headless browser; 620px and 500px layouts visually reviewed. No page errors.
- Installer is unsigned. Native install/upgrade/uninstall in a Windows guest is
  still unverified; project policy prohibits host installation.
- Spelling corrections are deterministic post-processing, not improved acoustics.
  GigaAM's repeated-syllable error on novel words remains a known limitation.

See [implementation](IMPLEMENTATION.md) and [RC3 notes](docs/releases/2.2.1-rc.3.md).
