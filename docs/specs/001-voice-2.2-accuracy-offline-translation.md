# Egoist Voice 2.2 — точная диктовка и локальный перевод

- Status: `APPROVED`
- Approved: `2026-08-01` — user explicitly replied: `Да, утверждаю обе.`
- Corrected architecture approved: `2026-08-02` — after reviewing the concrete
  GigaAM/Whisper/Hy-MT split the user replied: `Продолжай`.
- Target version: `2.2.0`
- Date: `2026-08-01`
- Supersedes after approval: accuracy/performance items from `docs/v2/02-spec.md`; does not declare the dirty 2.1.1 candidate released.

## 1. Observable outcome

Пользователь удерживает привычный trigger, говорит по-русски с английскими
именами и техническими терминами, отпускает trigger и получает грамотно
оформленный текст без потерянного начала/окончания. Явная команда
`переведи ... на <язык>` возвращает локальный перевод без сети и без запуска
Egoist Translate вручную.

Версия считается завершённой только когда одновременно выполнены corpus,
performance, stress, installer и clean-machine gates из раздела 12. Прохождение
unit tests само по себе не является доказательством точности или готовым релизом.

## 2. Пользователь и границы

### Для кого

- Один Windows-пользователь, который диктует русские сообщения, документы и
  технические/игровые тексты с английскими proper nouns.
- Локальная работа и приватность важнее минимального размера дистрибутива.
- Основной сценарий — push-to-talk; клавиатурный и mouse trigger сохраняются.

### Не для кого

- Не cloud transcription, meeting recorder или многопользовательский server.
- Не автоматический литературный редактор, который меняет смысл сказанного.
- Не скрытая отправка аудио, текста или telemetry наружу.

## 3. Подтверждённая исходная точка

- Текущий код: WPF/.NET 8, GigaAM v3 как основной RU ASR и условный
  Whisper large-v3-turbo fallback для mixed speech.
- Capture создаёт `WaveInEvent` на каждое нажатие; pre-roll и release tail
  отсутствуют. Это правдоподобная причина потери первых и последних фонем, но
  гипотеза должна быть подтверждена A/B-аудиотестом.
- Словари уже знают `Claude Code`, `Anthropic`, `OpenAI`, `GitHub` и другие
  термины, однако post-ASR exact/flexible replacement не гарантирует исправление
  произвольного внутреннего split или неверно распознанной основы.
- Translation сейчас зависит от отдельно установленного HY-MT server на
  `127.0.0.1:47821`; это не satisfies «всегда работает» и оставляет конфликт
  ownership/lifecycle между двумя приложениями.
- Свежая baseline-проверка `2026-08-01`: `387/387` Release tests passed.
- Private corpus отсутствует (`0` WAV и нет `reference.jsonl`), поэтому текущую
  жалобу пользователя нельзя честно объявить исправленной до записи корпуса.

## 4. Зафиксированные решения

### 4.1 Audio capture

1. Заменить per-press MME capture на постоянно прогретый WASAPI capture с
   bounded memory ring buffer. Начальные измеряемые значения: `200 ms`
   pre-roll и `350 ms` release tail; финальные значения выбираются на corpus,
   а не увеличиваются без ограничения.
2. Захватывать preferred device format и один раз качественно resample в
   16 kHz mono для ASR. Audio callback не блокирует UI, не выполняет inference,
   disk I/O или неограниченные allocations.
3. Аудио живёт в памяти; WAV разрешён только в явном local diagnostic mode и
   никогда не попадает в logs/repository.
4. Quiet-speech gate становится адаптивным к pre-roll noise floor и только
   отклоняет целиком бессодержательную запись. Он не обрезает фонемы.
5. AGC, aggressive denoise и VAD trimming не включаются по умолчанию: прошлые
   измерения проекта показывали регрессии. Любой такой stage допускается только
   как отдельно измеренный эксперимент.

### 4.2 ASR и proper nouns

1. GigaAM v3 остаётся RU primary, пока один и тот же corpus не докажет
   превосходство замены. Официальная модель обучена прежде всего под русский и
   остаётся наиболее подходящей исходной точкой.
2. Production ASR фиксируется: GigaAM v3 RNN-T остаётся RU primary, текущий
   Whisper large-v3-turbo остаётся условным mixed-language fallback. Qwen,
   fine-tuned Whisper и другие ASR downloads не входят в 2.2 и не являются
   зависимостью реализации.
3. На frozen corpus сравниваются только безопасные варианты текущего стека:
   baseline decoding, contextual hotwords текущего sherpa-onnx Transducer и
   deterministic vocabulary/entity repair. Hotwords активируются лишь после
   проверки exact GigaAM export/tokenizer и negative controls.
4. Built-in catalog расширяется минимум категориями AI/development, big tech,
   Windows/apps, gaming/studios/platforms. У каждого canonical term есть
   проверенные spoken aliases; user dictionary всегда имеет приоритет.
5. Split repair разрешён только для catalog-backed aliases с whole-token
   boundaries и negative controls. Fuzzy rewrite неизвестных слов запрещён.
6. Нельзя «исправлять» уже корректный английский текст или менять значение
   русских слов ради похожего brand name.

### 4.3 Команды, пунктуация и форматирование

1. Translation command — отдельный deterministic intent parser, а не substring
   search. Поддерживаются строгие prefix/suffix формы, например:
   - `переведи на английский: <текст>`;
   - `<текст>, переведи это на английский`;
   - `переведи следующую фразу на английский <текст>`.
2. Фразы, где слово «переведи» является частью содержания, не выполняются без
   полной grammar match. Positive и adversarial negative fixtures обязательны.
3. Voice punctuation commands и translation intent разбираются до финальной
   типографики, но после безопасной vocabulary normalization. Парсер возвращает
   typed result: payload, source/target language, command position и confidence
   class; ошибок через magic strings нет.
4. Финальный formatter консервативно исправляет casing, whitespace, duplicate
   punctuation и spoken newline/paragraph commands. Он не пересказывает речь.
5. Для длинной диктовки paragraph break ставится только по явной команде или
   подтверждённой длинной паузе на sentence boundary. Автоматические списки не
   угадываются из смысла.

### 4.4 Общий offline translation engine

Egoist Translate владеет реализацией и release artifact общего per-user
`Egoist Translation Engine Host`; Egoist Voice поставляет тот же pinned artifact
и остаётся самостоятельным приложением.

- Единственный host на пользователя: named mutex + Windows named pipe с
  `CurrentUserOnly`; имя и wire contract версионируются (`v1`).
- Минимальный async contract: `Handshake`, `GetStatus`, `Translate` и
  `Cancel(targetRequestId)` с correlated/idempotent dispositions. Каждая
  длительная операция имеет timeout и cancellation.
- Length-prefixed JSON messages имеют correlation/request id и size limit.
- Stable errors согласованы с host contract: `InvalidRequest`,
  `AmbiguousLanguage`, `IncompatibleClient`, `EngineMissing`, `ModelMismatch`,
  `EngineBusy`, `Cancelled`, `Timeout`, `OutputInvariantFailed`,
  `InternalError`; клиент не парсит текст ошибки.
- Внутренний `llama-server` слушает случайный loopback port, использует
  per-launch secret, недоступный logs, и никогда не является public app API.
- Модель/runtime принимаются только по manifest: model alias, source URL,
  license, exact byte size и SHA-256. Правило «самый большой `.gguf`» удаляется.
- Host сериализует GPU work, поддерживает priority `Interactive > Hover`;
  явный selection/clipboard request и Voice map в `Interactive`, hover — в
  `Hover`. Host отменяет obsolete hover requests и выгружает модель после
  измеренного idle timeout. ASR завершается до тяжёлого translation inference,
  чтобы они не боролись за GPU.
- Side-by-side engine versions и atomic `current` pointer позволяют безопасный
  upgrade. Installer каждого продукта создаёт owner marker; uninstall одного
  продукта не удаляет engine/model, пока существует второй owner.
- Ни source text, ни translation, ни audio не пишутся в application/installer
  logs. Метрики содержат только duration, byte/character counts и stable codes.

Если named-pipe ACL, private loopback authentication или owner-safe uninstall
нельзя доказать тестом, прямой общий port не принимается как fallback.

## 5. Translation model gate

Новая модель не выбирается по размеру или vendor claims. Frozen RU↔EN corpus
содержит обычную речь, AI/dev/gaming proper nouns, slang, punctuation,
paragraphs, Markdown/code/URLs/placeholders и adversarial instructions.

Сравниваются на одинаковом locked corpus:

- `Hy-MT2-1.8B-Q6_K` (~1.47 GB) как основной size/performance candidate;
- `Hy-MT2-1.8B-Q8_0` (~1.91 GB) как quality candidate;
- `Hy-MT2-1.8B-Q4_K_M` только как optional low-memory profile, если он
  проходит те же meaning/invariant gates.

Отсутствующая локально старая `HY-MT1.5-7B-Q8_0` не загружается ради
церемониального baseline и не блокирует новый engine. Сравнение Q6/Q8,
human blind review и protected-token gates остаются обязательными.

Q6 становится default только если paired gate против Q8 подтверждает отсутствие critical
meaning errors, неухудшение общей RU↔EN adequacy, не менее `98%` exact
proper-name preservation и `100%` preservation для placeholders/code/URLs и
явных line breaks. Любая hallucination, instruction leakage или silent language
swap — blocker. Quality tier поставляется только если его преимущество заметно
пользователю и оправдывает footprint.

## 6. Distribution и ресурсы

- Release artifact `Egoist Voice 2.2 Full Offline` включает ASR, выбранную
  translation model, host и native runtimes; после установки сеть не нужна.
- Предварительная честная оценка при 1.8B Q8 — более `3 GB`, а не обещанные
  ровно 2 GB: текущий Voice payload уже около 1.2 GB, translation model около
  1.91 GB, отдельно добавятся host/runtime/installer overhead. Точный размер
  фиксируется только из финального artifact.
- Translation model не загружается до первой команды. После idle unload host
  возвращает VRAM/RAM; idle Voice не держит Whisper и MT одновременно.
- Reference budgets до baseline freeze:
  - trigger → visible listening state: `p95 <= 100 ms`;
  - UI heartbeat во время capture/inference: `p95 <= 50 ms` stall;
  - pure-RU end-to-text latency: не хуже baseline более чем на `15%`;
  - warm translation 300 characters на release machine: `p95 <= 2 s`;
  - translation cold-ready на release machine: `p95 <= 15 s`;
  - idle translation host без модели: `<= 80 MB` private working set;
  - `30 min` dictation и `8 h` idle не дают unbounded memory/handle growth.

Если target hardware не выдерживает hard budget, результат документируется как
unsupported profile, а не скрывается усреднением.

## 7. UI/UX scope

Полного redesign нет. Сохраняются capsule, tray/settings и существующая visual
language; добавляются только состояния, необходимые для доверия и управления:

- `Распознаю`, `Уточняю термины`, `Перевожу на …`, `Готово`, typed local error;
- translation engine/model readiness и размер в settings;
- явный retry и `Вставить исходный текст` после translation failure — исходник
  никогда не вставляется молча как будто перевод успешен;
- microphone calibration/level hint без сохранения аудио;
- keyboard access, UI Automation names, visible focus, non-color-only status,
  DPI/contrast/reduced-motion checks.

Recognized payload после ошибки хранится только в памяти ограниченное время и
не сохраняется для sensitive targets.

## 8. Data and integrations

- Persistent user data: существующие settings/dictionary плюс versioned engine
  owner/model manifests. Миграции additive и обратимы.
- Private corpus, recordings, recognized/translated text не входят в Git,
  telemetry или release artifact.
- Единственная integration — локальный Translation Engine Host. Cloud API,
  analytics, accounts и credentials отсутствуют.

## 9. Corpus и метрики

Перед первой product change текущая 2.1.1 candidate фиксируется как baseline на
том же hardware и corpus. Минимальный private/user-voice gate:

| Set | Minimum | Главная проверка |
| --- | ---: | --- |
| ru-clean/fast/noisy | 45 utterances | WER и latency regression |
| quiet/sibilant/soft sounds | 30 | non-empty, WER, false gate |
| clipped beginnings/endings | 30 + 30 | ни одной обрезанной целевой фонемы |
| RU↔EN entities and split words | 60 | entity exact accuracy, split count |
| translation commands | 40 positive + 80 negative | recall/precision |
| long-form | 5 × 2–5 min | punctuation, paragraphs, memory |

Public/synthetic audio может расширять regression suite, но не заменяет
запись пользователя.

Ship thresholds относительно frozen baseline:

- ru-clean WER: не хуже более чем на `0.5 pp`;
- quiet/sibilant subset: минимум `15%` relative WER improvement и `>= 95%`
  accepted speech sessions;
- first/last target phonemes: `0` clipped cases in gate set;
- covered RU↔EN entity exact accuracy: `>= 90%`, а catalog negative controls:
  `0` false replacements;
- tagged split-word errors: минимум `50%` reduction;
- translation command: `>= 98%` recall на positives и `100%` precision на
  negative fixture set;
- punctuation/formatting: content words не меняются; punctuation F1 и manual
  correction count не хуже baseline, long-form preference проходит human check.

## 10. Failure behavior

- Missing/corrupt model, occupied resources, GPU loss, host crash, timeout,
  cancel и disk-full дают typed local error и сохраняют UI responsiveness.
- Translation failure не вставляет source silently; пользователь выбирает retry
  или original. ASR failure не оставляет stale clipboard.
- Повторный запуск host после crash не создаёт второй model process.
- Нельзя удалить shared runtime/model при uninstall, если другой product owner
  ещё установлен.
- Любой integrity/security failure — fail closed, без отправки текста на
  неизвестный localhost process.

## 11. Что человек делает вручную

1. Пользователь явно утверждает эту specification.
2. После подготовки recorder script пользователь записывает private gate corpus;
   assistant не читает и не публикует его содержание.
3. Пользователь отдельно утверждает implementation tickets.
4. Для публично подписанного installer пользователь предоставляет доступ к
   code-signing process/certificate; без него возможен только проверенный local
   unsigned artifact.
5. Внешняя публикация/download hosting требует отдельного подтверждения.

## 12. Verification и finish line

### Automated

- Unit/property tests для capture ring/tail, resampling, vocabulary boundaries,
  command grammar, formatter, IPC framing/errors/cancel/timeouts и ownership.
- Existing Release suite остаётся зелёной; build/publish/installer выполняются
  из чистого staging directory с pinned hashes.
- Corpus runner выдаёт machine-readable per-set WER, entity accuracy,
  command precision/recall, punctuation metrics, latency и resource report.
- Stress: 300 trigger cycles, 1,000 translate requests, concurrent Voice +
  Translate, cancellation storm, host/model crash/restart, 30 min capture,
  8 h idle, corrupt manifest/model and GPU fallback.

### Manual / release

- Clean Windows 10 22H2 and Windows 11 x64, standard-user install.
- Full Offline install with network disabled → RU dictation → RU/EN proper names
  → translation → app restart → upgrade from supported version → uninstall one
  owner → second app still translates → final uninstall leaves no running host.
- Real microphone checks for hotkey/mouse trigger, quiet speech, start/end
  phonemes, long-form formatting, DPI and keyboard-only settings.
- Independent verifier, который не писал implementation, даёт ship verdict.

Только после всех gates: bump `2.2.0`, release notes, reproducible hashes и local
final artifact. Tag/push/publication выполняются лишь по отдельной внешней
авторизации.

## 13. Исследовательские основания и неизвестные

- [Official GigaAM v3](https://huggingface.co/ai-sage/GigaAM-v3) остаётся
  baseline для русского; vendor WER не заменяет наш корпус.
- [Sherpa-ONNX hotwords](https://k2-fsa.github.io/sherpa/onnx/hotwords/index.html)
  документирует contextual biasing для Transducer + `modified_beam_search` —
  именно такого decoder path, который использует текущий GigaAM service.
- [Official Hy-MT2](https://github.com/Tencent-Hunyuan/Hy-MT2) и официальные
  [1.8B GGUF files](https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF/tree/main)
  дают подходящий ~2 GB candidate, но vendor quality claims не являются ship
  evidence.

Открытые вопросы, которые решаются измерением, а не предположением:
совместимость hotwords с exact GigaAM tokenizer/export, победитель Q6/Q8,
точные pre/tail durations, idle timeout, final footprint и budgets на minimum
hardware. Выбор production ASR больше не является открытым вопросом.

## Approval record

Пользователь явно утвердил обе спецификации `2026-08-01`. Это approval
разрешает подготовить context-sized tracer-bullet tickets, но не отменяет
отдельный ticket approval gate и не разрешает multi-GB downloads, product-code
changes, version bump или release до соответствующих этапов.

`2026-08-02` пользователь после явного объяснения скорректировал направление
словом `Продолжай`: не менять GigaAM + Whisper, не загружать новые ASR models,
добавить compact local MT и улучшать распознавание через capture, contextual
biasing, entity registry и deterministic formatting. Эта поправка заменяет
противоречащие ей ASR-candidate и HY-MT1.5 baseline gates.

До ticket approval wire-level cancellation уточнён как отдельная операция
`Cancel(targetRequestId)` и Voice закреплён как consumer общего versioned
Contracts artifact. Это не меняет утверждённый outcome/scope; уточнение устраняет
возможность двух несовместимых реализаций одной согласованной semantics.
