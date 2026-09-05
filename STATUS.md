# Egoist Voice — current status

- Updated: 2026-09-05 UTC.
- Release channel: **v2.2.0-preview.2**, unsigned public preview. Stable remains **2.1.0**.
- Publication scope: current EV-2224 application, Compact RU installer/portable and Full + Qwen.
- Source and all downloadable file hashes are bound in the release asset `release-manifest.json`.

## Current deliverables

Compact RU: 253.4 MB EXE, 283.8 MB ZIP, 518.5 MB unpacked / 506 files.
GigaAM INT8, CPU, Russian, self-contained .NET; settings stay in `Data` beside the EXE.
Full: 0.4 MB bootstrap, about 5.6 GB payload, three-part offline archive.
GigaAM, Whisper large-v3-turbo Q5, Qwen3-4B Q4_K_M, Hy-MT2 Q8 and pinned llama.cpp.
Fresh Full enables Qwen and disables audio history; upgrades preserve existing settings.

The Full DLL is identical to installed EV-2224:
`7dca5f3955e1acaaa13c5e1339bca66ee9a4437eafd5b63c192448a8331af572`.
All 129 application source files match its accepted source snapshot.

## Fresh evidence and limits

- Release tests: **634/634 pass**, zero skipped.
- Compact installer: clean Windows Sandbox installation, payload hashes, offline ASR,
  AAC recovery, repair and uninstall preserving Data passed on the release bytes.
- Portable ZIP: all 506 entry hashes, sizes and paths verified.
- Full archive: native integrity test and all 22 extracted files match the original;
  extracted bootstrap verifies the complete payload without installation.
- Bootstrap HTTP download, interrupted transfer, resume and final hash fixture passed.
- README rendered and inspected; 11 screenshots show the actual WPF UI with synthetic data.

**Full clean installation lifecycle remains unverified.** The Sandbox was forcibly
terminated during installation; the owner explicitly requested publication without
repeating Sandbox testing. No installer was executed on the development workstation.
No stable 2.2.0 or improved acoustic accuracy claim is made. The whole-product quality
gate remains **HOLD** pending real voice, hardware/endurance and Full lifecycle evidence.

## Next work

Collect reproducible field reports against Preview 2; complete Full installation,
Qwen startup, repair/uninstall and Translator coexistence checks before a stable release.
Older program/ticket documents describe historical gates; this snapshot and current
source/release manifests own the current publication facts.

[Install](docs/INSTALL.md) · [User guide](docs/USER-GUIDE.md) ·
[Build](docs/BUILD.md) · [Release evidence](docs/releases/2.2.0-preview.2.md) ·
[Change history](docs/changes/INDEX.md)
