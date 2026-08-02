# Egoist Voice — architecture

## Scope

Локальное Windows dictation app записывает микрофон по hotkey/mouse trigger, распознаёт речь on-device, нормализует текст и безопасно вставляет его в активное приложение.

## Verified boundaries

- WPF app owns tray/settings, recording capsule, global input hooks and safe text delivery.
- Shared-mode WASAPI remains warm with a bounded 200 ms idle pre-roll. Release
  keeps a 350 ms tail, then downmixes/resamples once to 16 kHz mono in memory;
  ordinary dictation never writes WAV. Only explicit private-corpus recording
  may persist a completed take.
- Audio pipeline runs local GigaAM with conditional Whisper fallback,
  profile-aware deterministic entity repair, normalization and commands.
- Entity catalogue v2 is whole-token bounded. Safe names are global; ambiguous
  names require a local target/utterance domain and carry term-specific negative
  contexts. Exact split/join repairs replace no arbitrary edit-distance span.
- GigaAM contextual bias uses a pinned optional official SentencePiece model to
  generate exact Sherpa BPE resources. It is a paired-corpus candidate, not the
  interactive default; any integrity/native failure returns to baseline.
- Translator client optionally talks to verified local HY-MT service on port 47821 and owns sidecar/Job Object lifecycle when started.
- Installer packages self-contained .NET/native runtimes while large speech models live under user-local storage and survive upgrade.

## Целевые рамки и разделяемый движок (решения 2026-08-02)

- Voice остаётся на `net8.0-windows`. Совместимость обеспечивает Translator:
  `Egoist.Translation.Contracts` и `.Client` становятся мультитаргетными
  `net8.0;net10.0`. Voice ссылается только на них и никогда на `Core` или
  `EngineHost`.
- Порт `47821` — унаследованный путь доверия. Он заменяется защищённым
  named-pipe клиентом в `EV-2206` и удаляется только после доказанного
  паритета.
- Разделяемый движок перевода живёт в
  `%LOCALAPPDATA%\EGOIST\TranslationEngine\v1\` и принадлежит обоим
  приложениям через реестр владельцев. Установщик Voice пишет и удаляет
  **только** `owners\egoist-voice.owner.json`.
- ASR-модели Voice остаются в `%LOCALAPPDATA%\EgoistVoice\Models` и в каталог
  разделяемого движка не переезжают.
- Диктовка не зависит от перевода: при любом состоянии движка, включая его
  полное отсутствие, запись, распознавание, нормализация и вставка текста
  продолжают работать. Это проверяемое свойство `EV-2212`, а не побочный
  эффект.
- Нормативный текст модели владения —
  [`COEXISTENCE-CONTRACT.md`](../../egoist-translator/docs/program/COEXISTENCE-CONTRACT.md).

## Source of truth

- [`README.md`](../README.md) — product behavior, commands, privacy and release path.
- [`docs/HANDOFF-2.1.1.md`](./HANDOFF-2.1.1.md) — current exact dirty candidate and pending gate.
- [`docs/v2/`](./v2/) — audit, market, specification and roadmap.
- `.sln`, `.csproj`, source and tests — executable truth.

## Unknowns

- Любая деталь, не подтверждённая указанными источниками или свежей проверкой,
  считается `not verified` и не должна достраиваться по предположению.
