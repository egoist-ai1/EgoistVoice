# Egoist Voice — architecture

## Scope

Локальное Windows dictation app записывает микрофон по hotkey/mouse trigger, распознаёт речь on-device, нормализует текст и безопасно вставляет его в активное приложение.

## Verified boundaries

- WPF app owns tray/settings, recording capsule, global input hooks and safe text delivery.
- Shared-mode WASAPI remains warm with a bounded 320 ms idle pre-roll. Release
  keeps a 350 ms tail. Capture downmixes/resamples incrementally to 16 kHz mono in memory;
  ordinary dictation never writes WAV. Only explicit private-corpus recording
  may persist a completed take.
- Russian 2.4.0 uses plain GigaAM v3 RNNT INT8 for words plus E2E RNNT INT8 for audio punctuation/case, four CPU threads per engine, sequential greedy decoding. Bounded projection preserves primary words; conservative known-name spelling requires exact secondary audio confirmation. A serialized service owns initialization, cancellation, formatter fallback and 30-second retry backoff. Unicode native paths use pinned source UTF-8 bindings with unchanged stock native DLLs. Historical Full code retains conditional Whisper fallback,
  profile-aware deterministic entity repair, normalization and commands.
- GigaAM keeps batches of up to six comparable chunks, but decodes the final tail
  separately when it is shorter than half the longest member. A one-item batch uses
  single-stream decode. Temporary file-reader buffers are cleared in `finally`.
- Recording retains 16 kHz float blocks instead of the complete raw device stream.
  The existing WDL settings and end flush preserve bit-for-bit input parity in the
  tested formats. Stop allocates one contiguous float array for the existing ASR API;
  memory grows with duration and briefly includes both copies. Cancellation clears
  owned blocks. Quiet pre-roll/tail and chunk pauses
  use recording-relative levels. The stop sound plays after capture stops.
- The scarlet capsule uses eight FFT bands (70–8000 Hz) mapped onto fifteen tapered
  bars in a 256 × 48 DIP capsule, with an 8 ms attack and 80 ms release. Its waveform loop runs only while recording, budgeted to 60 Hz (10 Hz reduced motion); stable drawings and sub-pixel changes do not invalidate the visual. Timer text changes once a second;
  FFT buffers are reused and display analysis never modifies recognition samples.
- Literal mode is the default, including older settings without the new field. It
  preserves decoder text except an explicit trusted spelling pass for reported
  Egoist product-name aliases and the missing letter in `репозиторй`; it disables
  the general dictionary/commands/translation/Qwen and
  suppresses optional Whisper refinement. It cannot repair acoustic recognition errors.
- Optional automatic Qwen formatting preserves words, order, numbers, paths and negation.
  Manual spelling correction remains a reviewed proposal with conservative validation.
  An owned-host lease spans startup and generation. Production has no idle unload
  timer; explicit shutdown/settings changes own its lifetime. The automatic two-second budget
  includes startup waiting. Batch/microbatch limits are 512/256; context remains 2048.
- Manual Qwen correction can use a second word-preserving punctuation pass when the
  accepted correction needs sentence formatting. Both passes share one budget; a
  failed second pass retains the accepted correction. Critical pronouns are protected.
- Russian RC2 bundles GigaAM in `Models`; Qwen in `TextModels` and llama.cpp in
  `TextRuntime` are optional builder inputs, omitted from the default package.
  Explicit environment overrides take precedence, then
  bundled assets, then existing installed locations. This Compact-based package owns
  no shared translation engine. Its Inno defaults are seeded only for new settings.
- Entity catalogue v2 is whole-token bounded. Safe names are global; ambiguous
  names require a local target/utterance domain and carry term-specific negative
  contexts. Exact split/join repairs replace no arbitrary edit-distance span.
- Historical E2E GigaAM contextual bias used a pinned optional official SentencePiece model to
  generate exact Sherpa BPE resources. It is a paired-corpus candidate, not the
  interactive default. Plain RNNT uses 34 character tokens and does not load E2E BPE; hotword diagnostics reject unsupported requests.
- Translation commands use the hash-pinned shared `net8.0` client over the
  current-user named pipe. Voice may start the installed Host, but never owns,
  kills or logs its source/result payload.
- Installer packages self-contained .NET/native runtimes while large speech models live under user-local storage and survive upgrade.
- Russian RC3 uses an offline React/Electron shell around the same silent Inno
  engine. The shell owns its temporary profile, bounded IPC actions, payload hashes,
  folder selection and progress display. It blocks closing during installation;
  successful install and launch depend on native exit and process-start results.
  Electron is not installed into Voice. Native Inno remains the owner of uninstall,
  shortcuts, autostart and Restart Manager; explicit unchecked options remove the
  previous autostart value/owned desktop shortcut during an upgrade.

## Целевые рамки и разделяемый движок (решения 2026-08-02)

- Voice остаётся на `net8.0-windows`. Совместимость обеспечивает Translator:
  `Egoist.Translation.Contracts` и `.Client` становятся мультитаргетными
  `net8.0;net10.0`. Voice ссылается только на них и никогда на `Core` или
  `EngineHost`.
- Порт `47821` и прямой HTTP chat-completions удалены из достижимого Voice
  пути. `EV-2206` использует только проверенный current-user named pipe.
- Разделяемый движок перевода живёт в
  `%LOCALAPPDATA%\EGOIST\TranslationEngine\v1\` и принадлежит обоим
  приложениям через реестр владельцев. Установщик Voice пишет и удаляет
  **только** `owners\egoist-voice.owner.json`. Full Offline setup consumes the
  exact Translator bundle and commits Host -> Q8/runtime pack -> owner; reuse
  requires complete manifest/hash/file-set verification. Because the preserved
  ASR + GPU + MT payload exceeds Inno's single-file ceiling, Inno creates
  private build-time slices. A small versioned bootstrap embeds them into one
  outer EXE, verifies footer/manifest and every segment SHA-256 before launch,
  checks temporary-drive space, forwards arguments, waits for completion and
  removes its task-owned extraction directory.
- GitHub delivery reuses those exact private Inno files without recompressing
  or changing product bytes. `EgoistVoiceWebBootstrap` prefers a complete
  colocated set for Full Offline use; otherwise it downloads from one pinned
  release tag into a versioned user-local cache, resumes `.part` files when
  GitHub honors Range, restricts redirects to HTTPS GitHub asset hosts and
  launches Inno only after declared size and SHA-256 pass. Success removes the
  online cache; download or install failure preserves it for retry.
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
