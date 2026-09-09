# Egoist Voice — current status

- Updated: 2026-09-09 UTC.
- Candidate: **2.2.1-rc.1**, offline Russian installer and portable app.
- Branch: `codex/scarlet-voice-quality`; payload source: `92af68a`.
- Local packaging authorized and completed. No tag, push, publication or host installation.

## Current deliverable

`artifacts/Russian-2.2.1-rc.1-final/EgoistVoice-Setup-Russian-2.2.1-rc.1-win-x64.exe`
is one 2,673,881,982-byte package. Includes Compact/GigaAM, Qwen3-4B, llama.cpp
CPU/Vulkan and .NET 8.0.30. An adjacent `portable` folder is also usable.
The previous installed app and original source checkout remain untouched.

Clean logo, scarlet FFT capsule, quiet-boundary handling, conservative Russian text
repair and bounded Qwen lifetime are included. New changes avoid padded short ASR
tails, protect pronouns and finish accepted corrections within a shared formatting
budget. New-install Qwen defaults preserve existing settings on upgrade.

## Verification and limits

- Full: 847 passed, 0 skips. Compact: 840 passed, 7 Full-only skips.
- Builder: 3 Pester tests; PSScriptAnalyzer clean; self-contained publish successful.
- Paired synthetic ASR: long-clip latency −26.6%, CPU time −20.1%; all 48 pairs
  produced identical text. One CPU and synthetic voice, no general accuracy claim.
- Final bundled models: normal/−18 dB fixture, four runs each, zero word errors;
  native Qwen 13/13 controls passed. Expanded correction remains 17/19 by words.
- All 534 staged file hashes and all three embedded installer segments verified.
- Unsigned RC. Clean Windows install/upgrade/uninstall unverified: Sandbox/VM absent.
  Real microphone, whispered/mumbled speech and GPU matrix remain unverified.

## Next action

Run the installer lifecycle in an isolated clean Windows guest and evaluate real
microphone speech before a stable release. See [implementation](IMPLEMENTATION.md)
and [RC notes](docs/releases/2.2.1-rc.1.md) for exact evidence and limitations.

[Build](docs/BUILD.md) · [User guide](docs/USER-GUIDE.md) · [Changes](docs/changes/INDEX.md)