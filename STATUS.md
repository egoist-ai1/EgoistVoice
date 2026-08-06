# Egoist Voice — status

- Last updated: `2026-08-06T19:06:39Z`
- Version/revision: Published version remains `2.1.0`; source/local test build is
  `2.2.0`; work is preserved on `v2.2-wip` (pre-task checkpoint `f4984f6`).
- Stage: `local test build / EV-2206 pass; offline ASR setup built; user field test next`

## Observable outcome

Локальное Windows dictation app записывает микрофон по hotkey/mouse trigger, распознаёт речь on-device, нормализует текст и безопасно вставляет его в активное приложение.

## Current milestone

- Current release slice: [`EV-2206`](./docs/tickets/EV-2206-shared-translation-client.md)
  is implemented and independently accepted locally. Voice now uses the
  protected shared Engine and an owner-safe installer; the user field-tests
  the built candidate next.
- Approved spec: [`docs/specs/001-voice-2.2-accuracy-offline-translation.md`](./docs/specs/001-voice-2.2-accuracy-offline-translation.md),
  approved on 2026-08-01 and corrected by the user's `Продолжай` on 2026-08-02.
- Approved breakdown: [`docs/tickets/README.md`](./docs/tickets/README.md);
  GigaAM + conditional Whisper are fixed production ASR and no new ASR model
  download is in scope.
- Baseline handoff: [`docs/HANDOFF-2.1.1.md`](./docs/HANDOFF-2.1.1.md); the dirty candidate remains unreleased.
- Current self-contained continuation:
  [`docs/changes/2026-08-06T190639Z-ev-2206-voice-220-test-build.md`](./docs/changes/2026-08-06T190639Z-ev-2206-voice-220-test-build.md).
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
- Replaced the legacy trusted-port/HTTP/sidecar path with the hash-pinned
  `net8.0` shared Contracts/Client artifact and current-user named pipe.
  Voice starts an installed shared Host when needed but never owns or kills it.
- Translation failures are typed and fail closed: the original dictated text
  is no longer silently inserted as if it were a successful translation.
  Unsupported offline languages are rejected before source text is framed.
- Voice setup now registers/removes only
  `owners\egoist-voice.owner.json`; it never deletes the shared Engine tree or
  Translator's owner. The build verifies exact client DLL sizes/hashes.
- Built the unsigned `2.2.0` offline-ASR installer with the exact GigaAM and
  Whisper payload: `1,283,478,780` bytes, SHA-256
  `9cdea920583a7d04efa8b54bb420e4bc2941ff8c78b6a7665a53390da89cc4fe`.
  It was not executed on the development host. Independent review passed.

## Next safe action

1. Пользователь устанавливает и проверяет три локальных пакета: shared Engine,
   Translator и Voice 2.2.0; модель перевода активируется в Translator.
2. После обратной связи — завершить оставшийся EV-2205 formatting slice.
3. `EV-2207` → `EV-2208` → `EV-2212`.
4. `EV-2209` full-MT packaging → `EV-2214` оснастка → `EV-2213` независимость
   совместно с Translator `T020`.
5. **Для измеренного финального SHIP, не для текущего field test:** записать
   приватный корпус `EV-2201` (350 клипов) и прогнать парные гейты
   `EV-2202`/`EV-2203` до любого измеренного утверждения о точности и до
   финального релиза.

## Verification

| Date (UTC) | Check | Result | Evidence |
| --- | --- | --- | --- |
| 2026-08-06 | EV-2206 independent outcome review | pass-local | Pinned net8 named-pipe client, shared-host non-ownership, fail-closed delivery, bounded language tier, owner-safe ISS and vendor-integrity build gate accepted. |
| 2026-08-06 | Voice 2.2.0 guarded unsigned setup | pass-local | 1,283,478,780 bytes; SHA-256 `9cdea9...cc4fe`; exact 900,364,167-byte ASR model payload; 466/466 tests; installer compiled but was not executed. |
| 2026-08-06 | Voice Release build | pass | 0 warnings, 0 errors; changed-file format clean. |
| 2026-08-02 | EV-2200 repository safety net | pass | Branch `v2.2-wip` at `b7281fd`; `main` remains `f1f5997`; 99 changed/new files preserved; no GGUF/WAV/private-key extensions staged. |
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
  EV-2205 has a residual formatting slice; EV-2206 is locally complete;
  EV-2207…EV-2210 retain their remaining gates. No final public 2.2.0 `SHIP`
  claim is possible before microphone, corpus, packaging, clean-machine and
  independent EV-2210 gates.
- EV-2205 is only partially complete: `TranscriptNormalizer`,
  `TranscriptFormatter`, voice formatting commands and GigaAM chunk paragraph
  joining still require the conservative/idempotent implementation and tests
  described in the active ticket.
- Voice consumes exact project-local `net8.0` shared client artifacts and stays
  independently buildable. Updating them requires a new manifest/hash review.
- The Voice test installer contains complete offline ASR but not the 1.78 GiB
  MT model; translation requires the separate shared Engine setup and model
  activation through Translator. EV-2209 retains the single full-MT bundle.
- Candidate work is now recoverable on local branch `v2.2-wip` at `b7281fd`;
  `main` and release 2.1.0 remain untouched. Tag, packaging and publish remain
  unapproved.
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
