# Программные документы (указатель)

Канонические межпроектные документы принадлежат проекту `egoist-translator`,
потому что там живёт разделяемый Translation Engine. Здесь только указатель —
копий нормативного текста в Voice нет и быть не должно.

| Документ | Путь |
| --- | --- |
| Утверждённые решения 2026-08-02 | [`../../../egoist-translator/docs/program/DECISIONS-2026-08-02.md`](../../../egoist-translator/docs/program/DECISIONS-2026-08-02.md) |
| Программный план до финальных `.exe` | [`../../../egoist-translator/docs/program/PROGRAM-PLAN.md`](../../../egoist-translator/docs/program/PROGRAM-PLAN.md) |
| Контракт сосуществования и жизненного цикла | [`../../../egoist-translator/docs/program/COEXISTENCE-CONTRACT.md`](../../../egoist-translator/docs/program/COEXISTENCE-CONTRACT.md) |
| Матрица проверки установки и удаления | [`../../../egoist-translator/docs/program/SANDBOX-TEST-MATRIX.md`](../../../egoist-translator/docs/program/SANDBOX-TEST-MATRIX.md) |
| Спецификация иконок | [`../../../egoist-translator/docs/program/BRAND-ICON-SPEC.md`](../../../egoist-translator/docs/program/BRAND-ICON-SPEC.md) |

## Что из этого обязательно для Voice

- **Решение D1** — Voice остаётся на `net8.0-windows`; Translator делает
  `Contracts` и `Client` мультитаргетными. `EV-2206` ссылается на грань
  `net8.0` и не тянет `Core`/`EngineHost`.
- **Решение D3 и контракт сосуществования** — установщик Voice обязан
  регистрировать себя владельцем движка и при удалении убирать **только свой**
  owner-файл. `EV-2209` и `EV-2213` проверяют это.
- **Решение D4** — паки моделей ставятся по закреплённым хэшам. Это касается
  только модели перевода; ASR-модели GigaAM/Whisper остаются там, где они
  сейчас, и в `EGOIST\TranslationEngine\packs` не переезжают.
- **Матрица** — строки `C-02` и `C-03` описывают главный сценарий пользователя:
  удаление Translator не должно ломать голосовой перевод в Voice.
- **Иконки** — `EV-2211` исполняет спецификацию; исходный растр 1024×1024
  кладётся в `assets/brand/` и коммитится.

При расхождении между этим указателем и каноническим документом прав
канонический документ.
