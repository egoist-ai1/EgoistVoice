# Egoist Voice — current status

- Updated: 2026-09-09 UTC.
- Final version: **2.2.1**, promoted from RC3 at the owner's explicit request.
- Repository: [egoist-ai1/EgoistVoice](https://github.com/egoist-ai1/EgoistVoice).
- Distribution: [v2.2.1](https://github.com/egoist-ai1/EgoistVoice/releases/tag/v2.2.1).
- Payload/shell source: `05d9f4b0d24f7c3a86a5ee7ff8f28bc1dd8ead9f`.

## Deliverable

`artifacts/React-2.2.1-release/EgoistVoice-Setup-Russian-2.2.1-win-x64.exe`

353,118,713 bytes; SHA-256
`26da4171d10a5e93a6dd429f79fbeb1388f67cd235b6bf044baf57847155fe12`.

The offline package includes Compact/GigaAM and .NET 8.0.30. React/Electron belongs
only to the installer. Qwen, Whisper and the translation engine are not bundled.
Behavior is unchanged from RC3: scarlet capsule, streaming capture/file reading,
literal default and bounded trusted spelling fixes.

## Verification and limits

- Final source: 30 focused release/literal/spelling tests passed, no skips.
- Final native publish, Inno compile, React build and Electron packaging passed.
- All 507 payload files and two Inno segments verified; PE versions read back.
- Final UI: four states, toggle and mocked install transition, 620/500px layouts,
  no page errors. Native installer was not executed.
- Earlier RC2: Full 879 passed; Compact 872 passed, 7 Full-only skips.
- Unsigned. Clean Windows install/upgrade/uninstall remains unverified.
- Publication as final is explicitly authorized; historical Full EV-2210 gates
  are not relabelled passed. Acoustic errors and live-recording RAM limits remain.

See [release notes](docs/releases/2.2.1.md), [implementation](IMPLEMENTATION.md) and
[build instructions](docs/BUILD.md).
