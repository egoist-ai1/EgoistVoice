# Egoist Voice — status

- Last updated: `2026-08-02T16:37:11Z`
- Version/revision: Published/source version `2.1.0`; Git `main` at `f1f5997`;
  dirty preserved 2.1.1 candidate plus Voice 2.2 specs/harness.
- Stage: `implementation / EV-2205 parser gate complete, formatter active; human quality gates pending`

## Observable outcome

Локальное Windows dictation app записывает микрофон по hotkey/mouse trigger, распознаёт речь on-device, нормализует текст и безопасно вставляет его в активное приложение.

## Current milestone

- Active ticket: [`EV-2205`](./docs/tickets/EV-2205-commands-and-formatting.md)
  — strict translation/voice commands and conservative human-like formatting.
- Approved spec: [`docs/specs/001-voice-2.2-accuracy-offline-translation.md`](./docs/specs/001-voice-2.2-accuracy-offline-translation.md),
  approved on 2026-08-01 and corrected by the user's `Продолжай` on 2026-08-02.
- Approved breakdown: [`docs/tickets/README.md`](./docs/tickets/README.md);
  GigaAM + conditional Whisper are fixed production ASR and no new ASR model
  download is in scope.
- Baseline handoff: [`docs/HANDOFF-2.1.1.md`](./docs/HANDOFF-2.1.1.md); the dirty candidate remains unreleased.
- Current self-contained continuation:
  [`docs/changes/2026-08-02T161158Z-resume-handoff.md`](./docs/changes/2026-08-02T161158Z-resume-handoff.md).
- Программный план до финальных установщиков:
  [`PROGRAM-PLAN.md`](../egoist-translator/docs/program/PROGRAM-PLAN.md).
  Первый шаг новой сессии: [`docs/KICKOFF.md`](./docs/KICKOFF.md).
- Approved amendment:
  [`docs/specs/002-voice-2.2-brand-ui-and-independence.md`](./docs/specs/002-voice-2.2-brand-ui-and-independence.md),
  утверждён `2026-08-02`; добавляет `EV-2200` и `EV-2211`–`EV-2214`.

## Completed in the latest task

- Fixed the approved architecture: GigaAM v3 remains RU primary, current
  Whisper remains conditional fallback; no replacement ASR/download is in 2.2.
- Replaced per-press MME/WAV capture with continuously warm shared-mode WASAPI,
  a bounded 200 ms pre-roll and 350 ms release tail.
- Normal dictation now stays in memory and is downmixed/resampled once to
  16 kHz mono. Both GigaAM and conditional Whisper consume that same array;
  only explicit corpus recording may persist WAV.
- Adaptive pre-roll calibration now accepts sustained quiet speech and a
  peak-aware low-energy-consonant path without AGC, denoise or audio trimming.
- Added ring/session/cancel/isolation/conversion/gate coverage including 300
  bounded cycles. Release tests pass `411/411`; real default-device WASAPI smoke
  passed with 12,960 in-memory samples.
- Pinned the optional official GigaAM SentencePiece artifact, generated exact
  Sherpa BPE/hotword resources and proved native baseline/candidate startup.
- Recorded [`Decision 001`](./docs/decisions/001-gigaam-contextual-bias-hold.md):
  the current 403-phrase score-1.15 candidate is compatible, but interactive Voice stays
  baseline until identical private WAVs prove the quality and latency gates.
- Added catalogue v2 for AI/dev/apps/companies/gaming with local technology and
  gaming profiles, term-specific ordinary-language blockers, canonical casing
  and exact observed split/join repair. User rules still compile last.
- Normal window and corpus paths now share profile-aware post-processing.
  Synthetic ASR integration passed 6/6 positive entities and preserved 4/4
  negative controls; this does not replace the private user-voice gate.
- EV-2205 translation intent now returns typed direction, prefix/suffix position
  and match class. Bare ambiguous prefixes require an explicit boundary;
  suffixes require a completed-clause boundary; command mentions remain text.
- The locked command fixture passes 40/40 positives and 80/80 adversarial
  negatives. Payload nouns and terminal sentence punctuation are preserved.

## Next safe action

1. `EV-2200` — зафиксировать текущие ~68 изменённых файлов в ветке `v2.2-wip`.
   Проверить `.gitignore` до `git add`: WAV, приватный корпус и модели в
   историю не попадают. `main` остаётся на `f1f5997`. Выполняется **первым**.
2. Finish EV-2205 protected-span normalization, conservative duplicate
   punctuation and pause-confirmed paragraph formatting; add idempotence tests.
   Зависимостей от Translator нет — можно вести сразу после `EV-2200`.
3. `EV-2211` — конвейер иконки; параллельно, как только пользователь положит
   исходный растр 1024×1024 в `assets/brand/`.
4. `EV-2206` — после того как Translator выполнит решение D1 (мультитаргет
   `net8.0;net10.0` для `Contracts`/`Client`) и закроет `T008`. Не ссылаться
   на несовместимые бинарники и не поднимать Voice до `net10.0`.
5. `EV-2207` → `EV-2208` → `EV-2212`.
6. `EV-2209` упаковка → `EV-2214` оснастка → `EV-2213` независимость
   совместно с Translator `T020`.
7. **Действие пользователя, которое агент выполнить не может:** записать
   приватный корпус `EV-2201` (350 клипов) и прогнать парные гейты
   `EV-2202`/`EV-2203` до любого измеренного утверждения о точности и до
   финального релиза.

## Verification

| Date (UTC) | Check | Result | Evidence |
| --- | --- | --- | --- |
| 2026-08-02 | Release unit/integration suite | pass | Fresh `466/466`; app and tests built in Release. |
| 2026-08-02 | Locked translation-command gate | pass | `40/40` positives, `80/80` negatives; focused parser suite `82/82`. |
| 2026-08-02 | `git diff --check` | pass | No whitespace errors; only LF→CRLF notices. |
| 2026-08-01 | Release unit/integration suite | pass | Fresh `458/458`; Release app/test build 0 warnings/errors. |
| 2026-08-01 | Entity positive synthetic ASR smoke | pass-limited | One Windows TTS WAV produced exact Anthropic, Claude Code, DeepSeek, GitHub, Android Studio and Counter-Strike after normal profile/dictionary processing. |
| 2026-08-01 | Entity negative synthetic ASR smoke | pass-limited | Separate WAV preserved гражданский кодекс, курсор, стимул and мета-анализ; synthetic voice is not corpus evidence. |
| 2026-08-01 | Native GigaAM paired smoke after catalogue v3 export | pass-hold | Baseline and 403-phrase candidate initialized; both silence outputs empty. |
| 2026-08-01 | Release unit/integration suite | pass | Fresh `453/453`; Release app/test build 0 warnings/errors. |
| 2026-08-01 | Native GigaAM paired smoke | pass-hold | Baseline and 400-phrase hotword v2 recognizers initialized; both returned 0 characters for 500 ms silence; activation still corpus-gated. |
| 2026-08-01 | Corpus runner decoder contract | pass | Explicit `baseline|hotwords`; reports pin mode/version/score; PowerShell parser passed. |
| 2026-08-01 | Release unit/integration suite | pass | Fresh `411/411`; Release app/test build 0 warnings/errors. |
| 2026-08-01 | Actual default-device WASAPI smoke | pass | 12,960 in-memory 16 kHz samples; pathless normal capture; quiet room correctly classified no-speech. |
| 2026-08-01 | EV-2202 synthetic resource/order gate | pass | Ring wrap/alignment/clear, tail ordering, cancellation isolation, 300 cycles, stereo 48 kHz conversion and adaptive quiet/noise controls. |
| 2026-08-01 | `dotnet test .\tests\Egoist.Voice.Tests\Egoist.Voice.Tests.csproj -c Release --no-restore --nologo` | pass | Fresh: 402 passed, 0 failed, 0 skipped; Release app/test build succeeded. |
| 2026-08-01 | Corpus JSON Schema validation | pass | 363/363 shipped JSONL nodes valid; 0 invalid. |
| 2026-08-01 | Corpus inventory | blocked-human | 350 scripted clips, but 0 WAV, no `reference.jsonl`, no baseline artifact. |
| 2026-08-01 | Privacy/determinism contracts | pass | Canary text/path omitted from reports; sensitive scope flows across async work; repeated normalized fixture reports are byte-identical. |
| 2026-08-01 | Baseline runner syntax | pass | PowerShell 7 and Windows PowerShell 5.1 parser accepted the script. |
| 2026-08-01 | `git diff --check` | pass | No whitespace errors; only existing LF→CRLF notices. |
| 2026-08-01 | `doctor.ps1 -ProjectAudit -ProjectPath .\projects\egoist-voice -Strict` | pass | Fresh read-only audit: `egoist-voice ok`. |
| 2026-07-27 | Installer/release lifecycle | not-run | Handoff explicitly says publish, payload and clean-machine lifecycle are pending. |

## Blockers and residual risks

- EV-2201 is not done: the user must produce the private 350-WAV corpus and real 2.1.1 aggregate baseline. Accuracy, quiet-speech and first/last-phoneme claims remain unproven until then.
- The baseline runner deliberately does not download models. Missing pinned
  current models produce a stable local failure; alternative ASR downloads are
  removed from the approved 2.2 scope.
- EV-2202 deterministic implementation is complete, but user-voice metrics,
  device removal and 30-minute endurance are not yet run. EV-2203 compatibility
  is complete but its paired quality verdict is HOLD; EV-2204 deterministic
  implementation is complete but its private accuracy verdict is pending;
  EV-2205 is active and EV-2206…EV-2210 remain dependency-gated. No final 2.2.0
  claim is possible before microphone, corpus, packaging, clean-machine and
  independent EV-2210 gates.
- EV-2205 is only partially complete: `TranscriptNormalizer`,
  `TranscriptFormatter`, voice formatting commands and GigaAM chunk paragraph
  joining still require the conservative/idempotent implementation and tests
  described in the active ticket.
- Translator's shared assemblies currently target `net10.0`; Voice targets
  `net8.0-windows`. EV-2206 is blocked until a compatible target or explicit
  multi-target contract/client is built and verified.
- **Dirty candidate не зафиксирован в Git.** Около 68 изменённых файлов поверх
  `f1f5997` существуют только в рабочем каталоге примерно с 27 июля; один
  `git checkout` или `git clean` уничтожает их безвозвратно. Снимается тикетом
  `EV-2200` (коммит в ветку `v2.2-wip`) первым же шагом. Tag, packaging и
  publish по-прежнему не разрешены.
- Existing `manage-project-history.ps1` does not parse under Windows PowerShell 5.1 because its UTF-8-without-BOM em dash is decoded incorrectly; continuity checkpoints use installed PowerShell 7.6.4 after recording the expected 5.1 failure.

## Durable context

- [Architecture](./docs/ARCHITECTURE.md)
- [Application map](./docs/APP_MAP.md)
- [Durable context](./docs/CONTEXT.md)
- [Roadmap](./docs/ROADMAP.md)
- [Specifications](./docs/specs/README.md)
- [Tickets](./docs/tickets/README.md)
- [Change history](./docs/changes/INDEX.md)
- [Release notes](./docs/releases/README.md)
