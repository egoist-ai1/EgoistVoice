# Tickets

The user approved the complete Voice 2.2 breakdown on 2026-08-01 with
`Приступай` and approved the corrected model architecture on 2026-08-02 with
`Продолжай`. GigaAM + conditional Whisper are fixed production ASR. Recording
the private corpus remains a release gate, but no longer blocks implementation
of deterministic capture/context/formatting work.

| Order | Ticket | State | Result | Depends on |
| ---: | --- | --- | --- | --- |
| 0 | [EV-2200](./EV-2200-repository-safety-net.md) | Approved; выполняется первым | Текущая работа зафиксирована в ветке `v2.2-wip`; правки обратимы | — |
| 1 | [EV-2201](./EV-2201-corpus-baseline.md) | Harness complete; human corpus pending release gate | Reproducible private baseline harness | Approved spec |
| 2 | [EV-2202](./EV-2202-boundary-safe-capture.md) | Implemented; private/endurance release gates pending | WASAPI memory pre/tail capture and adaptive quiet gate | Approved correction |
| 3 | [EV-2203](./EV-2203-asr-candidate-gate.md) | Candidate implemented; HOLD baseline pending paired corpus | Contextual-bias gate on current GigaAM + Whisper stack | EV-2202 deterministic checks |
| 4 | [EV-2204](./EV-2204-entity-accurate-asr.md) | Implemented; private accuracy gate pending | Safe RU/EN entity registry and repair | EV-2203 baseline decision |
| 5 | [EV-2205](./EV-2205-commands-and-formatting.md) | Active | Precise commands, punctuation and long-form layout | EV-2202, EV-2204 deterministic path |
| 6 | [EV-2206](./EV-2206-shared-translation-client.md) | Implemented locally; independent pass | Protected Engine Host v1 client replaces fixed-port trust; VM coexistence pending | Translator T002 contract + T008 host artifact |
| 7 | [EV-2207](./EV-2207-translation-ux.md) | Approved; pending | Honest, accessible translation and recovery states | EV-2206 |
| 8 | [EV-2208](./EV-2208-resource-arbitration.md) | Approved; pending | ASR/MT lifecycle meets latency and memory budgets | EV-2204, EV-2206 |
| 9 | [EV-2209](./EV-2209-full-offline-packaging.md) | Local split-package field build ready; single full-MT bundle pending | Full Offline installer and owner-safe shared runtime | EV-2207, EV-2208; Translator T010 pinned engine pack |
| 10 | [EV-2210](./EV-2210-release-gate.md) | Approved; pending independent gate | Independently verified local 2.2.0 artifact | EV-2201…EV-2209 |

## Амендмент 002 — облик и независимость (утверждён 2026-08-02)

Эти тикеты закрывают часть исходного запроса, которую `EV-2201–EV-2210` не
покрывали, и требование независимости двух приложений. Спецификация:
[Амендмент 002](../specs/002-voice-2.2-brand-ui-and-independence.md).

| Order | Ticket | State | Result | Depends on |
| ---: | --- | --- | --- | --- |
| 11 | [EV-2211](./EV-2211-brand-icon-pipeline.md) | Approved; ждёт исходный растр | Детерминированный конвейер иконок; новая иконка во всех местах | EV-2200 |
| 12 | [EV-2212](./EV-2212-ui-refresh.md) | Approved; pending | Честные состояния перевода; диктовка не зависит от движка | EV-2207, EV-2211 |
| 13 | [EV-2214](./EV-2214-sandbox-verification-harness.md) | Approved; pending | Повторяемый прогон матрицы установщика с доказательствами | EV-2209 |
| 14 | [EV-2213](./EV-2213-coexistence-lifecycle.md) | Approved; pending | Удаление Translator не ломает голосовой перевод в Voice | EV-2209, EV-2214; совместно с Translator T020 |

`EV-2211` можно вести параллельно сразу после `EV-2200`: он не зависит от
движка. `EV-2210` не имеет права выдать вердикт `SHIP`, пока
`EV-2211`–`EV-2214` не закрыты.

Historical references:

- Remaining 2.1.1 evidence is in [`docs/HANDOFF-2.1.1.md`](../HANDOFF-2.1.1.md).
- The older broad roadmap is in [`docs/v2/03-roadmap.md`](../v2/03-roadmap.md).

One ticket is implemented and checkpointed per clean context. No ticket may
silently absorb a failed gate or unrelated release work.

## Approval record

Recommended exact statement:

> Утверждаю полный breakdown EV-2201–EV-2210 для Egoist Voice 2.2 и разрешаю
> последовательную локальную реализацию product code по одному ticket, начиная
> с EV-2201, с обязательной проверкой и continuity checkpoint после каждого
> ticket. Это approval не разрешает multi-GB downloads для EV-2203,
> external/production deployment, publishing/tag/signing или пропуск
> независимого EV-2210; эти действия требуют отдельного подтверждения. EV-2209
> разрешён только как локальная RC-сборка, а local final 2.2.0 — только после
> verdict `SHIP` в EV-2210.

The user supplied the unambiguous equivalent `Приступай` after approving both
specifications. The 2026-08-02 correction expressly removes new ASR-model
downloads from scope and allows the separately designed ~2 GB local MT pack.
It does not authorize external publication, tagging, signing or skipping
EV-2210.
