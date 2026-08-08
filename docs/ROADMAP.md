# Egoist Voice — roadmap

Программный контекст обоих продуктов и порядок фаз до финальных установщиков —
в [`PROGRAM-PLAN.md`](../../egoist-translator/docs/program/PROGRAM-PLAN.md);
указатель на межпроектные документы — [`docs/program/README.md`](./program/README.md).

## Now

- `EV-2200`: зафиксировать текущие ~68 несохранённых файлов в ветке
  `v2.2-wip`. Выполняется **первым**: сейчас неделя работы существует только в
  рабочем каталоге. Коммит в ветку не является релизом.
- `EV-2201`: the 350-clip offline corpus harness is verified; private recording
  remains required before accuracy/release claims. **Записать корпус может
  только пользователь** — это единственный релизный блокер, который не
  решается кодом.
- `EV-2202`–`EV-2204`: deterministic capture, optional contextual-bias candidate
  and profile-aware entity repair are implemented; private voice/endurance and
  paired quality gates remain release requirements.
- `EV-2205`: typed strict translation-command parsing passes the locked 40/80
  fixture. User-observed product-name, inline punctuation and dotted-identifier
  regressions are fixed; protected-span normalization and pause-confirmed
  paragraph formatting remain the wider active slice.
- `EV-2206`: protected shared-host client is implemented and independently
  accepted locally; the old port/HTTP/sidecar path is gone.
- Voice `2.2.0` Full Offline field package is ready: both ASR models, GPU/CPU
  runtimes and the exact shared Engine/Q8 pack are inside one integrity-checked
  outer EXE. Internal Inno slices never leave build staging. Exact VM execution
  is not-run.
- Public `v2.2.0-preview.1` delivery exposes those same internal files as
  GitHub-compatible assets behind a hash-pinned web/offline bootstrapper. This
  is a field-test channel, not `EV-2210 SHIP`; stable remains `2.1.0`.
- Preserve the current dirty 2.1.1 candidate as the tested baseline; do not publish or silently fold it into a release.

## Next

- Finish EV-2205 with idempotent code/URL/placeholder preservation, strict
  formatting commands and paragraphs only on confirmed pause + sentence
  boundaries.
- User field test of the unsigned Full Offline Voice/Translator candidates.
- `EV-2214` guest harness -> `EV-2213`/Translator `T020` exact coexistence
  matrix; then `EV-2207` -> `EV-2208` for recovery UX/resource arbitration.

## Амендмент 002 — облик и независимость

Утверждён `2026-08-02`; спецификация
[`002`](./specs/002-voice-2.2-brand-ui-and-independence.md).

- `EV-2211` — конвейер фирменной иконки. Идёт параллельно сразу после
  `EV-2200`, как только пользователь положит исходный растр 1024×1024 в
  `assets/brand/`.
- `EV-2212` — честные состояния перевода в интерфейсе; отдельно проверяется,
  что диктовка работает при любом состоянии движка, включая его отсутствие.
- `EV-2214` — оснастка проверки установщика в Windows Sandbox и Hyper-V VM.
- `EV-2213` — независимость и жизненный цикл; выполняется **совместно** с
  Translator `T020`. Доказывает главный сценарий: удаление Translator не
  прерывает голосовой перевод в Voice ни сразу, ни после перезагрузки.

`EV-2210` не выдаёт `SHIP`, пока `EV-2211`–`EV-2214` не закрыты.

## Release gate

- Select ASR and MT models only by paired corpus evidence.
- Build the Full Offline installer and verify concurrent Egoist Voice + Egoist Translate ownership, upgrade and uninstall on clean Windows 10/11.
- `EV-2210`: run corpus, performance, stress and independent ship review; only then declare 2.2.0 final.

## Later / explicitly out of this milestone

- Cloud transcription/translation, semantic LLM rewriting and a full visual redesign.
- Public publishing or signed distribution without separate external authorization and signing access.

Completed detail belongs in `docs/changes/` or user-facing release notes, not
in this roadmap.
