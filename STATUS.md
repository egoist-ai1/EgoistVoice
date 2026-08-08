# Egoist Voice — status

- Last updated: `2026-08-07T23:59:57Z`
- Version/revision: stable published `2.1.0`; public preview target
  `v2.2.0-preview.1` from `v2.2-wip`.
- Stage: `unsigned public field-test candidate; publication transaction in progress;
  clean-VM lifecycle not-run`.

## Observable outcome

Локальное Windows-приложение записывает микрофон по hotkey/mouse trigger,
распознаёт речь on-device, детерминированно нормализует текст и безопасно
вставляет его в активное приложение. Явная голосовая команда переводит через
защищённый shared Engine.

## Installed and built identity

- Windows uninstall registry and the running process report installed
  `Egoist Voice 2.2.0`, `FileVersion=2.2.0.0`; this proves installed startup,
  not the downloadable install/upgrade lifecycle.
- Latest Full Offline build:
  `EgoistVoice-Setup-2.2.0-win-x64.exe`, `3,191,770,964` bytes,
  SHA-256 `eb2b2323ee8f77e234956711ab7b8eaf20fc5c2cd318e6380e87cb59eac14b9c`.
- The exact outer package was not executed on the development host by policy.

## Current verified implementation

- GigaAM is RU primary and Whisper is a conditional mixed RU/EN fallback.
- Translation uses exact project-local net8 Contracts/Client over the
  current-user named pipe. Dictation remains independent when Engine/model is
  missing or unavailable.
- Full Offline setup installs Host -> exact Q8/runtime pack -> `egoist-voice`
  owner. Uninstall removes only Voice's owner and preserves other owners/shared
  payload.
- Product-name repair, strict inline punctuation and sentence casing preserve
  ordinary `точка входа/доступа`, `Vue.js`, `example.com`, `config.json` and
  other dotted identifiers.
- Public delivery uses a small web/offline bootstrapper plus the same three
  payload files extracted byte-for-byte from the verified outer package. Local
  colocated payload wins; otherwise the bootstrapper downloads from the pinned
  GitHub tag with resume, verifies size/SHA-256 and launches only after success.

## Public preview delivery

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `EgoistVoice-Setup-2.2.0-inner.exe` | 1,762,824 | `0fd1706666f1411404799308f70c4bd6f82d9e801c07177f24f8bca49399175a` |
| `EgoistVoice-Setup-2.2.0-inner-1.bin` | 2,098,236,672 | `691f5e60b76b7595dc8752a2264bccda04ebb9d9dbc5e1257eb9a300846b2a02` |
| `EgoistVoice-Setup-2.2.0-inner-2.bin` | 1,091,700,448 | `6d7cada0e16fcec94899811d30fd386d816945f8185e62a3c20aab05b468c74c` |

The final web bootstrap hash is generated from the clean release commit and
recorded in `SHA256SUMS-2.2.0-preview.1.txt` plus the release receipt. Every
asset remains below GitHub's 2 GiB per-file limit.

## Fresh verification

| Date (UTC) | Check | Result |
| --- | --- | --- |
| 2026-08-07 | Voice Release suite | `480/480` pass |
| 2026-08-07 | Full Offline footer/manifest/3 payload SHA-256 | pass |
| 2026-08-07 | Web bootstrap offline discovery/hash path | pass |
| 2026-08-07 | Local HTTP download + forced `.part` + Range resume + final hash | pass |
| 2026-08-07 | PowerShell/YAML/README links/`git diff --check` | pass |
| 2026-08-07 | Social preview render | `1280x640`, inspected |
| 2026-08-07 | Development-host installer execution | `not-run` by policy |

## Next safe action

1. Complete the guarded GitHub transaction: push release commit, tag
   `v2.2.0-preview.1`, upload exact assets and verify anonymous download hashes.
2. Run EV-2214 against these hashes in Windows Sandbox and Hyper-V, then close
   EV-2213/T020 with timestamped coexistence/uninstall evidence.
3. Continue EV-2205/EV-2207/EV-2208/EV-2212 and the private corpus before any
   stable `2.2.0` or measured-accuracy claim.

## Residual risks and blockers

- Exact clean install/start/repair/upgrade/coexistence/uninstall of these bytes
  has not been observed in guest Windows.
- Candidate is unsigned and may trigger SmartScreen or Smart App Control.
- The private 350-WAV corpus, device-removal/endurance and broader formatting
  gate remain incomplete; `EV-2210` has not issued `SHIP`.
- The legacy .NET Framework bootstrap compiler is non-deterministic; the release
  receipt binds its exact bytes to the clean source revision and `sourceTreeDirty=false`.

## Durable context

- [Architecture](./docs/ARCHITECTURE.md)
- [Application map](./docs/APP_MAP.md)
- [Durable context](./docs/CONTEXT.md)
- [Roadmap](./docs/ROADMAP.md)
- [Release note](./docs/releases/2.2.0-preview.1.md)
- [Change history](./docs/changes/INDEX.md)
