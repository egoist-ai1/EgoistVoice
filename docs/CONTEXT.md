# Egoist Voice — durable context

Last reviewed: `2026-08-02`

This file routes a new task to verified project truth. It is not a transcript
and does not authorize skipping ticket or release gates.

## Active durable context

| ID | Status | Kind | Scope | Statement or route | Canonical owner | Evidence | Verified at | Expires |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `CTX-001` | `active` | `decision` | `project:egoist-voice` | GigaAM v3 remains the Russian primary ASR and the existing Whisper Large v3 Turbo remains conditional fallback; no replacement ASR belongs to Voice 2.2. | [`Voice 2.2 spec`](./specs/001-voice-2.2-accuracy-offline-translation.md) | [`architecture correction`](./changes/2026-08-01T213947Z-corrected-model-architecture.md) | `2026-08-02` | `none` |
| `CTX-002` | `active` | `constraint` | `project:egoist-voice` | Accuracy, quiet-phoneme and final-release claims require the private 350-WAV corpus; synthetic TTS is limited evidence. | [`EV-2201`](./tickets/EV-2201-corpus-baseline.md) | [`corpus harness note`](./changes/2026-08-01T210153Z-ev-2201-corpus-harness.md) | `2026-08-02` | `none` |
| `CTX-003` | `active` | `decision` | `project:egoist-voice` | The 403-phrase GigaAM contextual-bias candidate is compatible but production remains baseline until paired private WAV quality and latency pass. | [`Decision 001`](./decisions/001-gigaam-contextual-bias-hold.md) | [`EV-2203 note`](./changes/2026-08-01T222453Z-ev-2203-contextual-bias-hold.md) | `2026-08-02` | `none` |
| `CTX-004` | `active` | `fact` | `project:egoist-voice` | Profile-aware exact entity repair is implemented for technology/gaming names; user dictionary rules retain final precedence. | [`EV-2204`](./tickets/EV-2204-entity-accurate-asr.md) | [`EV-2204 note`](./changes/2026-08-01T223138Z-ev-2204-entity-profiles.md) | `2026-08-02` | `none` |
| `CTX-005` | `superseded-by-CTX-012` | `constraint` | `project:egoist-voice` | Voice must consume the verified shared Translator host contract; current Translator assemblies target `net10.0` and cannot yet be referenced by Voice `net8.0-windows`. | [`EV-2206`](./tickets/EV-2206-shared-translation-client.md) | [`resume handoff`](./changes/2026-08-02T161158Z-resume-handoff.md) | `2026-08-02` | `none` |
| `CTX-006` | `superseded-by-CTX-007` | `constraint` | `project:egoist-voice` | The dirty 2.1.1/2.2 candidate is not a release: do not reset, commit, tag, package or publish it without the corresponding gate and authority. | [`STATUS`](../STATUS.md) | [`2.1.1 handoff`](./HANDOFF-2.1.1.md) | `2026-08-02` | `none` |
| `CTX-007` | `active` | `decision` | `project:egoist-voice` | Кандидат 2.1.1/2.2 фиксируется коммитом в ветку `v2.2-wip` (тикет `EV-2200`), чтобы работа стала восстановимой. `main` и линия 2.1.0 не трогаются. Коммит в ветку не является релизом; тег, упаковка и публикация по-прежнему требуют своего гейта и отдельного разрешения. | [`EV-2200`](./tickets/EV-2200-repository-safety-net.md) | утверждённое поручение пользователя `2026-08-02` | `2026-08-02` | `none` |
| `CTX-008` | `active` | `decision` | `program:egoist` | Решение D1: Voice остаётся на `net8.0-windows`; Translator делает `Contracts` и `Client` мультитаргетными `net8.0;net10.0`. Это способ устранения `CTX-005`. | [`DECISIONS`](../../egoist-translator/docs/program/DECISIONS-2026-08-02.md) | утверждённый ответ пользователя `2026-08-02` | `2026-08-02` | `none` |
| `CTX-009` | `active` | `constraint` | `program:egoist` | Удаление EGOIST Translator не имеет права прервать голосовой перевод в Voice ни сразу, ни после перезагрузки. Установщик Voice удаляет только свой owner-файл и никогда чужой. Нормативный текст — контракт сосуществования. | [`COEXISTENCE-CONTRACT`](../../egoist-translator/docs/program/COEXISTENCE-CONTRACT.md) | [`EV-2213`](./tickets/EV-2213-coexistence-lifecycle.md) | `2026-08-02` | `none` |
| `CTX-010` | `active` | `decision` | `project:egoist-voice` | Амендмент 002 утверждён 2026-08-02: иконка (`EV-2211`), состояния перевода в интерфейсе (`EV-2212`), оснастка (`EV-2214`) и независимость (`EV-2213`). `EV-2210` не выдаёт `SHIP` без них. | [`Амендмент 002`](./specs/002-voice-2.2-brand-ui-and-independence.md) | [`tickets README`](./tickets/README.md) | `2026-08-02` | `none` |
| `CTX-011` | `active` | `constraint` | `project:egoist-voice` | Диктовка не зависит от перевода: при любом состоянии движка, включая полностью отсутствующий, запись, распознавание и вставка текста продолжают работать. Это отдельно проверяемое свойство. | [`EV-2212`](./tickets/EV-2212-ui-refresh.md) | [`Амендмент 002`](./specs/002-voice-2.2-brand-ui-and-independence.md) | `2026-08-02` | `none` |
| `CTX-012` | `active` | `fact` | `project:egoist-voice` | EV-2206 consumes exact project-local `net8.0` Contracts/Client DLLs through a hash-bound manifest and only the current-user named pipe. Voice starts but never owns/kills the shared Host; translation failures return before text delivery. | [`EV-2206`](./tickets/EV-2206-shared-translation-client.md) | [`EV-2206 test-build note`](./changes/2026-08-06T190639Z-ev-2206-voice-220-test-build.md) | `2026-08-06` | `none` |

## Promotion candidates

None.

## Rules

- Load only the active ticket and rows relevant to the task.
- Full rationale remains in the canonical owner linked above.
- Add a new row and immutable note when superseding a durable fact or decision.
