# Приватный корпус Egoist Voice 2.2

Этот корпус фиксирует качество до изменений ASR и capture pipeline. Без одной и той же начитки
нельзя доказать, что новая модель действительно лучше: меняется голос, темп, микрофон и фон, а не
только код. Полный baseline EV-2201 строится по **350 записям одного пользователя**.

WAV и вычитанный `reference.jsonl` являются приватными. Они исключены через `.gitignore`, не
попадают в app.log и не должны прикладываться к issue или коммиту. В Git остаются только
обезличенный скрипт, JSON Schemas, тесты и при необходимости aggregate-only отчёт.

## Состав

| Набор | Фраз | Что проверяет |
| --- | ---: | --- |
| `ru-en-mixed` | 60 | `Claude Code`, `Anthropic`, OpenAI, игровые и инженерные proper nouns |
| `ru-numbers` | 15 | даты, версии, числа и единицы |
| `ru-clean` | 20 | обычная русская речь в тихой комнате |
| `ru-commands` | 10 | пунктуационные и форматирующие voice commands |
| `ru-fast` | 15 | быстрый естественный темп |
| `ru-noisy` | 15 | обычный рабочий фон |
| `ru-longform` | 5 | записи 60–180 секунд, паузы, абзацы и склейка |
| `ru-quiet-phonetics` | 30 | тихие окончания, шипящие, мягкие согласные |
| `boundary-start` | 30 | первый звук сразу после нажатия |
| `boundary-end` | 30 | последнее окончание перед отпусканием |
| `translate-positive` | 40 | команды перевода, которые обязаны сработать |
| `translate-negative` | 80 | похожие фразы, которые не должны запускать перевод |
| **Всего** | **350** | полный frozen baseline |

Формат аудио совпадает с приложением: WAV PCM, 16 кГц, 16 бит, mono. Частичная начитка полезна
только как сохранённый прогресс; она не считается baseline и не разблокирует EV-2202.

## Versioned schema

Первая содержательная строка `script.jsonl` — manifest:

```json
{"kind":"schema","version":2,"privacy":"private-local-only"}
```

Каждый `set` объявляет точный `expectedCount`. Каждая фраза имеет безопасный стабильный `id`,
`text` для показа и может задавать:

- `expected` — грамотный эталон, если отображаемый prompt намеренно отличается;
- `tags` и `entities` — срезы и proper nouns;
- `translationCommand` — ожидаемое решение command parser;
- `boundary` (`start`/`end`) и `boundaryTarget` — проверяемое первое/последнее слово.

Runtime отклоняет неизвестные поля/kind, дубли, неверные counts, небезопасные или абсолютные пути,
неполный reference и несовпадение SHA-256 скрипта. Формальные схемы лежат рядом:

- `corpus-script-v2.schema.json`;
- `corpus-reference-v2.schema.json`;
- `benchmark-report-v2.schema.json`.

## Запись и baseline одной командой

Закройте обычный Egoist Voice: два процесса не должны делить микрофон. Из корня проекта запустите:

```powershell
.\scripts\run-corpus-baseline.ps1 -Record
```

Команда собирает Release, открывает recorder, после полной начитки проверяет все 350 WAV и
автоматически выполняет offline benchmark. Во время benchmark сеть и model download запрещены:
если pinned модели ещё не установлены, команда завершится стабильной ошибкой вместо скрытого
скачивания. Готовый отчёт атомарно фиксируется в
`artifacts/bench/baseline.json`; существующий baseline не перезаписывается без явного `-Force`.

В recorder удерживайте `Space`, пока говорите. `Backspace` возвращает на предыдущую фразу,
`Esc` сохраняет прогресс и выходит. Следующий запуск продолжит с первого отсутствующего WAV.

Правила начитки:

1. Говорите своим обычным голосом и темпом; не переходите на дикторскую манеру.
2. Оговорились — перезапишите фразу. Эталон должен соответствовать реально сказанному.
3. Для `ru-noisy` используйте реальный рабочий фон, а не искусственный шум.
4. В `boundary-start` начинайте сразу после нажатия, в `boundary-end` отпускайте сразу после
   последнего звука.
5. После записи вычитайте `reference.jsonl` вручную. Никогда не подставляйте туда вывод ASR — это
   измерило бы согласие модели с самой собой.

Если запись уже полная, повторный aggregate-only прогон выполняется без окна:

```powershell
.\scripts\run-corpus-baseline.ps1 -OutputPath .\artifacts\bench\latest.json -Label voice-2.1.1-repeat
```

Контекстные hotwords проверяются на тех же WAV отдельным явным режимом; интерактивное приложение
не включает их до положительного paired verdict:

```powershell
.\scripts\run-corpus-baseline.ps1 -NoBuild -DecoderMode hotwords
```

В результате `baseline.json` и `hotwords.json` имеют одинаковый corpus SHA-256, а поле
`parameters.gigaAmContextualBias` вместе с version/score не позволяет перепутать два прогона.

Для проверки уже собранного executable используйте `-NoBuild`. `-Force` допустим только при
осознанной замене frozen файла тем же corpus hash.

## Что содержит отчёт

`benchmark-report-v2` хранит stable clip IDs и числовые признаки, но никогда не сериализует
reference/hypothesis, WAV, абсолютные пути или exception message. В отчёте есть:

- общий и per-set WER/CER, failed clips;
- entity exact accuracy и split-word count;
- precision/recall команды перевода;
- punctuation F1 и точность start/end boundaries;
- p50/p95 latency;
- SHA-256 корпуса, скрипта, executable и pinned моделей;
- версия runtime/OS, CPU/architecture и resource snapshots;
- точные параметры текущего hybrid pipeline, contextual-bias version/score и явный
  `modelDownloadAllowed=false`.

`CorpusGateTests` сравнивает только отчёты с одинаковым corpus SHA-256 и останавливает регрессию,
если общий/per-set WER вырос более чем на 0,5 п.п. или p95 latency — более чем на 15 %. Отсутствие
приватного baseline на чужой машине означает skip, а не выдуманный pass.
