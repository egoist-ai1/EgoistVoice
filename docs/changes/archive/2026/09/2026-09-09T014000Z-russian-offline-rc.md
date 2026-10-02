# Russian offline RC with measured ASR and correction improvements

Date: 2026-09-09 UTC. Local installer build was explicitly requested; no publication.

## What and why

Avoid padding the final short GigaAM chunk to a full batch member; clear temporary
file-reader buffers. Improve Russian correction prompt and protect pronouns. Finish
accepted corrections through a separate word-preserving punctuation pass within the
same budget. Preserve a valid correction when sentence formatting fails.

Prefer bundled Qwen/model runtime after explicit overrides. Clean the logo and rebuild
ICO/PNG/installer bitmap. Package self-contained Compact, speech/text models, official
llama.cpp runtime and licenses into one verified EXE. Seed fresh settings only; remove
global image-name task kills and let Restart Manager handle the installed files.

## Validation

Full 847/847; Compact 840 passed with 7 intentional Full-only skips. Builder Pester 3/3,
PSScriptAnalyzer clean. First full packaging exposed non-measurable ordered dictionary
file records; changed them to PSCustomObject and completed a fresh build successfully.

Paired AB/BA long-clip latency 1599.56 → 1173.59 ms; CPU −20.1%. All 48 paired texts
equal. Six synthetic fixtures under four signal conditions, including targeted numeric
value checks 12/12. Final staged ASR normal/−18 dB: eight runs, zero word errors.
Final bundled Qwen 13/13 controls passed; expanded correction 17/19 word-level,
16/19 exact formatting. All 534 payload hashes and three embedded segments verified.

## Contracts, files and risks

Owners: `Services/GigaAmTranscriptionService.cs`, `Services/LocalTextFormatter.cs`,
`Services/LocalQwenHost.cs`, their focused tests, `scripts/Build-RussianInstaller.ps1`,
Compact staging/build scripts, `installer/EgoistVoiceCompact.iss`, project version/assets.
Source payload revision `92af68a`; docs explain new ownership and settings behavior.

Artifact and complete limits: [IMPLEMENTATION.md](../../IMPLEMENTATION.md).
Installer is unsigned. No clean Windows installation/upgrade/uninstall was possible
without Sandbox/VM; host installation prohibited by project contract. Synthetic evidence
does not establish accuracy for real quiet or unclear speech. Known Qwen agreement,
imperative and technical-formatting limits remain. No acoustic weights were changed.

Next: isolated Windows lifecycle, real microphone corpus and hardware matrix before
stable publication. Previous source/app and earlier candidate remain available.
