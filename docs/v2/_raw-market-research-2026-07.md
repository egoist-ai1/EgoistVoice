# Рынок десктопной голосовой диктовки, июль 2026

Дисклеймер по источникам: выдача по этой теме на 2026 год сильно засеяна SEO-контентом самих конкурентов (getvoibe, spokenly, weesperneonflow, softorbits, lumevoice, usevoicy, metawhisp). Всё, что взято оттуда, помечено **[конкурентный источник]**. Официальная документация, changelog'и и исходный код — первичные данные. `reddit.com` для фетча недоступен, поэтому пользовательские настроения восстановлены косвенно.

---

## 0. Карта рынка: четыре сегмента

| Сегмент | Кто | Экономика |
|---|---|---|
| **AI-native dictation** (ядро) | Wispr Flow, Aqua Voice, Willow Voice, Superwhisper | Подписка $8–15/мес, облако + LLM-постобработка |
| **Local-first, buy-once** | VoiceInk ($25–49), MacWhisper (€59), Whisper Notes ($6.99), Voisty | Разовая покупка, whisper.cpp/Parakeet локально |
| **OSS** | Handy (27.2k★), Buzz (19.7k★), Whispering, OpenWhispr, WhisperWriter | Бесплатно |
| **Легаси и ОС** | Dragon v16 ($699), Win+H, Voice Access, macOS Dictation, Talon | Бессрочная лицензия / встроено |
| **Смежное (не диктовка)** | Otter.ai, Notta, Vibe, Buzz | Meeting/файловая транскрипция |

---

# I. AI-NATIVE ЛИДЕРЫ

## 1. Wispr Flow — лидер рынка

**Платформы и цена** ([wisprflow.ai/pricing](https://wisprflow.ai/pricing)):
- Mac, Windows, iPhone, **Android** (вышел, на free — безлимит «limited time only»)
- Basic: бесплатно, **2 000 слов/нед** на десктопе, 1 000 на iPhone
- Pro: **$15/мес** или **$12/мес** годовых ($144/год); 14 дней Pro-триала без карты
- Enterprise: SOC 2 Type II + ISO 27001, SSO/SAML, enforced Privacy Mode
- Студентам: 3 месяца бесплатно + 50% скидка
- Компания подняла **$81M** ([wisprflow.ai](https://wisprflow.ai/))
- В российском App Store — 750 ₽/мес, 7 200 ₽/год ([Хабр](https://habr.com/ru/articles/1024634/))

### UI: «Flow Bar» — самое подробное, что удалось восстановить

Это не одно окно, а **два независимых слоя**:

**(а) Сигнал записи в точке фокуса.** По документации «Starting your first dictation» ([docs.wisprflow.ai](https://docs.wisprflow.ai/articles/6409258247-starting-your-first-dictation)): зажимаете хоткей → **слышите «ping» и видите движущиеся белые полоски** («white bars moving»). То есть обратная связь **двухканальная — звук + waveform**, и звук назван первым. Это важная деталь: Flow не заставляет смотреть на экран.

**(б) Flow Bar — постоянная плавающая панель.**
- Изначально **жёстко прибита к нижнему краю по центру**
- **9 июля 2026** это починили: панель стала **перетаскиваемой к левому или правому краю**, с запоминанием позиции. Причина в changelog названа прямо: «could land right on top of something you needed next, like the send button in Gmail», а на Mac она лежала поверх Dock ([wisprflow.ai/whats-new](https://wisprflow.ai/whats-new))
- **По умолчанию скрыта на новых установках** — включается в Settings
- **Hover раскрывает контекстные контролы**: языковой пикер (с марта 2026) и иконку «волшебной палочки» для Transforms
- Клик по панели запускает диктовку без хоткея

**(в) Уведомления как третий слой UI.** Flow активно общается тостами: «Don't tap. Hold down your shortcut» (до 3 раз), «Transcript Currently Processing», «Using Command Mode (beta)», предупреждения о микрофоне («mic unplugged / in use by another app / blocked by system settings» — каждое со своим предложенным фиксом), предупреждение о clamshell-режиме MacBook. В марте 2026 сделали **гранулярное отключение категорий уведомлений** и **галочку-подтверждение при клике по кнопке в уведомлении** — «previously it could feel unresponsive, and you'd wonder if the action actually went through» ([whats-new](https://wisprflow.ai/whats-new)).

**(г) Android Flow Bubble** — самый детально описанный UI у компании:
- Плавающий пузырь, появляется **только когда сфокусировано текстовое поле**, автоматически прячется вне полей
- **Tap-to-Dictate**: пузырь **разворачивается** в панель с Cancel (X) — waveform — Done (✓)
- **Hold-to-Dictate**: пузырь показывает **пульсирующий waveform**
- **Авто-сжатие через 5 секунд бездействия** в компактную иконку или «крошечную точку»
- **Слайдер прозрачности 20–100%**, слайдер размера (0.7x / 0.85x / 1.0x / 1.15x) с живым превью
- Драг к «snooze circle» внизу экрана прячет на 10 минут; вернуть — **встряхнуть телефон**
- Авто-минимизация в полях поиска, авто-скрытие в 50+ банковских приложениях
- Кнопка «копировать» рядом с пузырём после диктовки, авто-исчезает через 10 с

**(д) Scratchpad** (май 2026, beta) — блокнот, всплывающий по Option+S **поверх всего как стикер**, rich-text с вкладками и историей версий.

### Активация
- Mac: **hold Fn** (или Ctrl+Opt, если Fn нет). Hands-free: **Fn+Space** или **двойное нажатие** PTT-хоткея
- Windows: **hold Ctrl+Win**. Hands-free: Ctrl+Win+Space
- **Mouse Flow** (март 2026): любая непервичная кнопка мыши (Middle, Mouse4–Mouse10, Aux у Logitech), можно комбинировать с клавишей; отдельно можно повесить «Enter» на кнопку мыши
- Command Mode: Fn+Ctrl (Mac) / Ctrl+Win+Alt (Windows), до 4 шорткатов по 3 клавиши
- **Esc отменяет**, и Esc теперь перебиндивается (для Vim/терминала)
- Лимит сессии: предупреждение на 5 мин, стоп на 6 мин (было расширено до 20 мин в марте, затем в документации от 13 марта указано 5/6 — **противоречие в источниках**)

### Latency
Абсолютных цифр компания не публикует. Официально: **«Dictation latency is down 30% since the start of the year»** (9 июля 2026), uptime 99.9% ([whats-new](https://wisprflow.ai/whats-new)). **[конкурентный источник]** Willow оценивает Flow примерно в **~700 мс** ([willowvoice.com](https://willowvoice.com/blog/wispr-flow-review-voice-dictation)).

### ASR
Проприетарная **облачная**, локального режима нет вообще. Модель не раскрывается. В июне 2026 признались, что часть трафика шла на устаревшие модели («one too conservative on course correction and one too aggressive about changing words») — теперь все на одной. Строят **персонализированные речевые модели**, под это ввели новый переключатель Cloud Sync.

### Дифференциаторы
- **Auto Cleanup с четырьмя уровнями** (None / Light / Medium / High) — редкость: у большинства конкурентов это бинарный тумблер. Плюс **«Undo AI edit»** в истории, возвращающий сырой транскрипт
- **Command Mode**: выделить текст → сказать «make this more concise» / «translate to Polish» → замена на месте, Cmd+Z откатывает. Без выделения — генерация ответа inline. Лимит 1 000 слов
- **Голосовое изменение настроек**: «Add a rule to never use exclamation marks» → Flow показывает уведомление с кнопкой Apply, применяет только после подтверждения. Этого нет ни у кого
- **«Press enter»** — команда в конце диктовки, слова вырезаются, Enter нажимается
- **Transforms** — Polish и Prompt Engineer, можно свои, можно повесить на авто-применение после каждой диктовки
- **Styles** — распознаёт Instagram/Discord/Signal как личные мессенджеры, LinkedIn как рабочий, и меняет стиль письма
- **Insights** — WPM, стрик-хитмап, «communication archetype», лидерборд для Enterprise. Это геймификация, а не функция
- **Backtracking** — правки на лету («…в пятницу, нет, стоп, в четверг»)
- **Ранжирование микрофонов** + автопереключение при clamshell
- 100+ языков, персональный словарь со звёздочками и ранжированием по частоте, сниппеты
- Поддержка терминалов: Claude Code/Codex больше не схлопывают диктовку в `[Pasted N lines]`

### Что ругают
Wispr Flow — единственный в обзоре, кто **сам публично признал серию отказов**. Changelog за июнь 2026 читается как хроника кризиса:
- 4 июня: «Flow has been less reliable than it should be over the past few weeks», инфраструктурные изменения при масштабировании, failover не сработал
- 10 июня: два отдельных инцидента за две недели, «reallocating engineering resources to focus on stability»
- 17 июня: «Rapid growth in our user base strained our infrastructure», UK English и Swiss German маршрутизировались не туда, аудио-компрессия работала не так, как задумано
- 24 июня: «course corrections that didn't land, words swapped for ones you didn't say, punctuation or edits getting ignored»

**[конкурентные источники]** дополняют: Trustpilot **2.7/5**; паттерн «работает на триале, деградирует после оплаты» и «works 60% of the time»; **~800 МБ RAM и 8% CPU в простое** на Electron-сборке Windows; заморозка VS Code и Notepad++ во время диктовки ([eesel.ai](https://www.eesel.ai/blog/wispr-flow-pricing), [spokenly](https://spokenly.app/blog/wispr-flow-review), [getvoibe](https://www.getvoibe.com/resources/wispr-flow-review/)). Отдельно упоминается инцидент с r/ProductivityApps, где пользователь заявил об отправке скриншотов активного окна, а Flow забанил его — **проверить первично не удалось, считать слухом**.

Из официального списка Known Issues ([docs.wisprflow.ai](https://docs.wisprflow.ai/collections/5686269587-known_issues)) особенно показательны два: **«Missing first words in transcriptions»** и **«Flow fails to detect text fields or inserts incorrectly on non-QWERTY keyboard layouts»**.

---

## 2. Aqua Voice — самый технически прозрачный

**Цена** ([aquavoice.com/llms.txt](https://aquavoice.com/llms.txt), [aquavoice.com](https://aquavoice.com/)):
- macOS (Apple Silicon + Intel), Windows 10/11, iOS 17+
- Starter: бесплатно, **1 000 слов пожизненно** (~8 мин) — фактически триал
- Pro: **$8/мес** годовых ($96/год), 800 записей словаря
- Team $12, Enterprise (SSO/SAML, SCIM, Zero Data Retention)
- Студентам **70%** скидка. iOS отдельно $119/год
- **Avalon API**: OpenAI-совместимый эндпоинт, **$0.39 за час аудио**
- YC W24, Сан-Франциско, основатели Finn Brown и Jack McIntire

### UI
- **Floating bar / «pill»** (термины из changelog: «Floating Bar», «pill»). В январе 2026 чинили «Floating Bar positioning issue when in fullscreen» и «The microphone UI no longer jumps around when recording» — то есть панель была нестабильна и её стабилизировали
- **Чип над пилюлей** в Edit Mode: при удержании клавиши с выделенным текстом появляется маленький индикатор «12 words selected» — **элегантное решение проблемы «а что сейчас произойдёт»**
- **Aqua orb** — фирменный визуальный мотив, вынесен в лоадер и на экран входа (июнь 2026)
- Настройка «hide dock icon while running in the background»

### Два режима вывода — ключевая идея
- **Instant Mode**: нажал → говорил → отпустил → текст. Старт <200 мс, результат **~450 мс**
- **Streaming Mode**: слова **появляются в реальном времени по мере речи**, продолжают уточняться. Работает лучше всего с Deep Context

Формулировка из документации, объясняющая, почему оба нужны: *«You wouldn't sweat a Cursor prompt the way you would an important email… it's like texting style, some people send many short messages, others one long one.»*

### Активация
Hold **Fn** (Mac) / **Alt** (Windows). Несколько хоткеев на одно действие, до 5 клавиш в связке, поддержка F13–F19. Отдельный шорткат «Paste Last Transcript» (Cmd+Ctrl+V / Ctrl+Shift+→).

### Latency и ASR — единственные проверяемые цифры на рынке
- Старт **<50 мс**, вставка **~450 мс**
- Собственная модель **Avalon**, облачная, заменила Whisper-пайплайн в августе 2025
- **Avalon v1**: дебютировала #6 в общем зачёте и **#1 среди проприетарных** на независимом [Open ASR Leaderboard](https://huggingface.co/spaces/hf-audio/open_asr_leaderboard), 6.24% WER
- **Avalon 1.5** (апрель 2026): **5.55% WER**, вдвое быстрее, выигрывает **76%** слепых сравнений против ElevenLabs Scribe v2
- **AISpeak-10** (свой бенчмарк на AI/coding-терминологии): Avalon 97.4% против Scribe 78.8%, Whisper Large v3 65.1%, Canary 51.5%
- 49 языков (русский есть)
- SOC 2 Type II с 25 марта 2026

### Дифференциаторы
- **Deep Context** — клиентский движок читает экран. Пример из документации: «canonical title on the context response model» → `` `canonical_title` `` на `` `ContextResponse` ``. **Выключен по умолчанию**
- **Edit Mode** (18 июля 2026) — лучшая реализация голосового редактирования на рынке. Тот же хоткей, автоопределение по наличию выделения. Работает **без командных слов**: выделили «meet on Tuesday», сказали «meet on Monday» — Aqua понимает, что это замена. Правки **стекируются и откатываются** голосом («undo that», «go back to the original»). Осознанные исключения: адресная строка браузера и поля поиска (там всегда обычная диктовка), выделения >6 000 символов
- **File Tagging** — тегирует файлы в Cursor и Windsurf, когда вы их упоминаете
- **«Send it»** — голосовая отправка в streaming-режиме
- Custom Instructions в свободной форме («в iMessage и Slack пиши строчными, gen-z стайл»)
- Casual Messaging setting, надёжные @-упоминания в Slack голосом
- Локальная история; в privacy mode транскрипты редактируются at rest

### Что хвалят
Единственный продукт с **независимой внешней валидацией**. 9to5Mac провёл слепой тест на одном и том же тексте (речь Джобса в Стэнфорде): **Apple Dictation — 17 ошибок, Aqua — 1**, и Aqua ещё и расставила абзацы, тогда как Apple выдала сплошную простыню ([9to5mac.com](https://9to5mac.com/2025/08/15/aqua-voice-shows-just-how-good-mac-dictation-could-be-if-apple-just-tried/)). Andrej Karpathy, Product Hunt 5.0/5.

Отдельно ценен отзыв Colin Hughes, консультанта 9to5Mac по доступности: *«Dictation in Voice Control feels primitive by comparison… When a tool is more productive than the long-established king of dictation apps, Dragon, you start to take it very seriously.»* Его же критика — **Aqua не заменяет Voice Control, потому что не даёт навигации**.

### Что ругают
- **Только облако, офлайна нет ни на одном тарифе.** Ben Lovejoy (9to5Mac) назвал это первым из двух стоп-факторов, особенно в связке с Deep Context: *«The complete list of companies I trust to manage the privacy for that is as follows: Apple»*
- Free-план в 1 000 слов — это день использования
- Ошибки соединения, требующие перезапуска; один зафиксированный серверный аутейдж на ~20 минут
- Русский: **[Хабр]** «на английском впечатляет, но русский пока работает на стандартном движке» ([habr.com/ru/articles/1024634](https://habr.com/ru/articles/1024634/))

---

## 3. Superwhisper — единственный с внятной локальной опцией

**Цена** ([superwhisper.com](https://superwhisper.com/)):
- macOS, Windows 10/11, iOS. **Одна лицензия на все устройства и все платформы**
- Free: **навсегда**, диктовка в любое приложение, запись встреч, 100+ языков, малые локальные модели, кастомные промпты
- Pro: **$8.49/мес** или **$84.99/год**; **Lifetime $249.99**; студентам 40%
- Триал Pro — **всего 15 минут записи** (частая претензия)
- Enterprise: SOC 2 Type II, MDM (Jamf/Intune), SAML, SCIM

### UI — самый документированный и самый «оконный»

Официальная разбивка окна записи ([superwhisper.com/docs/get-started/interface-rec-window.md](https://superwhisper.com/docs/get-started/interface-rec-window.md)) — 7 элементов:
1. **Resize toggle** (на hover) — переключение между main и mini
2. **Audio waveform** — реальный, живой; документация прямо использует его как диагностику: «если waveform статичен, проверьте устройство ввода и разрешения»
3. **Status indicator** — цветная точка: **жёлтый = загрузка модели, синий = обработка, зелёный = готово**
4. **Mode display** — текущий режим + шорткат, кликабельно. В Super Mode здесь показывается **текущее приложение или сайт**
5. **Context capture indicator** — «загорается», подтверждая захват контекста буфера (если копировали в пределах 3 секунд до старта) или выделенного текста
6. Stop
7. **Cancel** — для записей **>30 секунд показывает подтверждение**, <30 с отменяет сразу

**Mini window** — компактный вариант, который можно оставить **всегда активным** даже в простое. На hover разворачивает три контрола: Change Mode / Start Recording / Expand. Те же контролы появляются **после завершения диктовки, если результат не был автоматически вставлен**. Правый клик даёт контекстное меню.

**Иконка в меню-баре** ([interface-menu-bar.md](https://superwhisper.com/docs/get-started/interface-menu-bar.md)) — цветная точка с **четырьмя** состояниями: жёлтый (загрузка модели), **красный (идёт запись)**, синий (обработка), зелёный (готово). Опционально: левый клик = toggle записи, правый = меню.

Итого у Superwhisper **три независимых индикатора состояния** (main window, mini window, menu bar), синхронизированных по цветовому коду. Это самая формализованная система статуса в категории.

### Активация ([settings-shortcuts.md](https://superwhisper.com/docs/get-started/settings-shortcuts.md))
Отдельные шорткаты на Toggle Recording, Cancel, Change Mode, **Push-to-Talk** (может делить хоткей с Toggle), **Mouse Shortcut с двойной семантикой**: быстрый клик = toggle, удержание = push-to-talk.

### ASR — самый широкий выбор ([models/voice.md](https://superwhisper.com/docs/models/voice.md))
- **Облако Superwhisper**: S1-Voice, Ultra (скорость 10/9, точность 9/9)
- **Локально Whisper через whisper.cpp**: от Fast (75 МБ, точность 1) до Ultra (3 ГБ, точность 10) и Ultra V3 Turbo (1.6 ГБ). **Nano и Standard бесплатны**
- **Nvidia Parakeet локально через WhisperKit**: 476/494 МБ, скорость 10, точность 8. Честное предупреждение в доке: «struggle with punctuation and have minor hallucination issues with single word recordings»
- **Deepgram Nova 3 / Nova 2 / Nova Medical** облаком
- LLM-постобработка: GPT-5, Claude Haiku 4.5, Llama 4, Grok 4.1, Gemini 3.0 Flash, Ministral — или **свои API-ключи**

### Дифференциаторы
- **Modes** — центральная концепция. Готовые (Voice / Message / Email / Note / Meeting / Super) + Custom с собственным промптом, выбором голосовой и языковой модели по отдельности, и **правилом авто-активации по приложению** («Activate when using: Telegram, WhatsApp»)
- **Super Mode** — адаптируется к содержимому экрана
- **Meeting assistant** с автозаметками, разделение по спикерам
- Транскрипция файлов, реобработка из истории
- Агентское кодирование: Claude Code, OpenCode, Pi, Codex, CLI
- Полноценный офлайн

### Что ругают
Windows-версия официально отстаёт ([windows.md](https://superwhisper.com/docs/get-started/windows.md)) — **нет FileSync, нет Hold Shift to Auto-Send, нет Simulate Keypresses, нет интеграции с агентскими кодинг-инструментами**. **[конкурентные источники]**: шаткий автопейст и отсутствие интерфейса словаря на Windows, крутая кривая входа, 15-минутный триал. Русский: **[Хабр]** «если не задать русский явно, приложение часто пытается перевести речь на английский», не вставляет текст в терминале VS Code ([habr](https://habr.com/ru/articles/1024634/)).

---

## 4. Willow Voice — главный ценовой удар 2026

**Цена** ([willowvoice.com/pricing](https://willowvoice.com/pricing)) — **7 июля 2026 диктовка стала бесплатной навсегда**:
- Basic: **бесплатно, безлимитно**, модель Frontier Mini, 20 использований Scribe в неделю
- Pro: $15/мес или **$12/мес** годовых — Frontier Pro, память стиля, безлимитный Scribe
- Business: $35 / $28 — enforced privacy mode, SOC 2 Type II, HIPAA
- macOS, Windows, iOS; Android скоро

Все обзоры с лимитом «2 000 слов/нед» на free — **устарели**.

### UI
Официально: «press and hold your hotkey (default is the **Function (fn)** key). You'll see **a small floating bar appear**» ([help.willowvoice.com](https://help.willowvoice.com/en/articles/10876920-dictating-with-willow-voice)).

Самый содержательный источник — **портфолио founding product designer Yiqi Yan** ([yiqyan.xyz/willow_desktop](https://yiqyan.xyz/willow_desktop)): плавающая панель ведёт к **трём функциям** — Dictation, Scribe (AI-переписывание/полировка/перевод), Main App Entry. Есть раздел «Dictation floating bar explorations» с видео-прототипами. Компания отдельно анонсировала «**The Bar**» как новый плавающий UI.

**Waveform, анимации, точное положение на экране, визуализация стадии распознавания — не найдено.** Косвенно: текст вставляется целиком после отпускания хоткея, значит **streaming-вставки нет**.

### Активация
- По умолчанию **hold fn**. До **4 хоткеев** одновременно, поддержка раздельных левого/правого Option и Command
- **Hands-Free Mode — двойной тап** хоткея (или отдельный хоткей)
- Отдельный хоткей для Willow Assistant

### Latency
Заявлено **«as little as 200ms»** — самая агрессивная цифра на рынке ([willowvoice.com](https://willowvoice.com/)). Технический блог CTO честно объясняет, что после оптимизации модели узким местом стала **системная латентность**: перемещение аудио, координация сервисов, вставка текста обратно, и даже физическое размещение дата-центров ([блог](https://willowvoice.com/blog/introducing-willow-frontier-pro)).

### ASR
Собственная облачная линейка: Atlas 1 → **Frontier Pro / Frontier Mini**. Архитектура двухступенчатая: **«best-in-class ASR» + собственная edit-модель**, обученная с нуля RL-техниками как lightweight speculative model. Базовая ASR **не раскрыта**. Оптимизируют не WER, а **edit rate** («cleanup tax») — концептуально интересная метрика. Локальный fallback для Pro на Mac и iOS.

### Дифференциаторы
Развёрнутый гайд по голосовым командам ([help.willowvoice.com](https://help.willowvoice.com/en/articles/13183983-voice-commands-and-automatic-formatting-guide)): нумерованные списки («One, bread. Two, milk»), буллеты, пунктуация голосом, «new paragraph», **backtracking на лету** («…still work for you? No wait, actually, 5 p.m.»), email-контекст с разбивкой на приветствие/тело/подпись. Плюс **Whisper mode** (работа с шёпотом), auto-learning dictionary, style-matching по приложениям, чтение кодового контекста.

**Противоречие в источниках**: сайт говорит 100+ языков, справка — 50.

### Что ругают
**[конкурентные источники, низкая достоверность]**: переписывает существующий текст, слово «delete» трактует как команду; проблемы с аббревиатурами (KPI, ROI, «SÉO» вместо «SEO»); неотключаемое уведомление о бездействии; непрошеный перевод при быстрой неанглийской речи; жалобы на iOS-клавиатуру, которая выше обычной из-за двух дополнительных рядов.

---

# II. LOCAL-FIRST

## 5. VoiceInk — эталон реализации на macOS

**$25 (1 Mac) / $39 (2) / $49 (3)**, разово, пожизненные обновления, 14 дней возврата. Только **Apple Silicon, macOS 14.4+**. GPL-3.0, [github.com/Beingpax/VoiceInk](https://github.com/Beingpax/VoiceInk), 5.7k★. Автор — Prakash Joshi Pax. Есть iOS-версия. 200k+ загрузок, 4.9 средний рейтинг ([tryvoiceink.com](https://tryvoiceink.com/)).

### UI — два стиля панели, переключаемых в настройках
Из исходников (`RecorderUIManager.swift`):
```swift
enum RecorderPanelStyle: String { case notch; case mini }  // дефолт .mini
```
Каждый обслуживается своим менеджером окна (`NotchWindowManager` / `MiniWindowManager`), оба создаются лениво. **При смене стиля на лету панель уничтожается, обнуляется и через 50 мс создаётся заново** — то есть это не один переиспользуемый компонент.

Состояния движка, отражаемые панелью: `.idle`, `.starting`, `.recording`, `.transcribing`, `.enhancing`, `.busy`. Нажатие хоткея во время `.starting/.transcribing/.enhancing` = **отмена**; в `.idle` при активной ассистент-сессии = **follow-up вопрос в той же панели**.

Notch-режим рисует силуэт Dynamic Island поверх физического выреза MacBook — паттерн реализуется через подкласс NSPanel: borderless, прозрачный, click-through, уровень `CGShieldingWindowLevel` (выше строки меню).

### Дифференциаторы
- **Power Modes / Modes** — автопереключение конфигурации по активному приложению **или URL**. Пример из документации: обнаружен gmail.com → активируется Email Mode с моделью Parakeet V3, промптом «Professional email», провайдером Gemini, включённым screen context и auto-send на Cmd+Return
- Контекст экрана и буфера обмена
- AI Assistant, Enhancement Modes (Polish / Email / Chat / Post)
- Личный словарь + Smart Replace
- Локально: whisper.cpp + FluidAudio (Parakeet). Облачное «улучшение» опционально и получает **только текст, не голос**

### Ограничения
Только macOS, только Apple Silicon. **PR не принимаются** — форкайте. 220 открытых issue. Нет мультиязычности сверх системной **[конкурентный источник]**.

## 6. MacWhisper — транскрибатор с диктовкой как бонусом

**€59 разово** через Gumroad, бесплатная база ([goodsnooze.gumroad.com/l/macwhisper](https://goodsnooze.gumroad.com/l/macwhisper)). Версия в Mac App Store называется Whisper Transcription и **не содержит диктовку** — Apple потребовала убрать Accessibility. 300 000+ пользователей.

UI режима Dictation в текстовом виде не задокументирован (только видео на Vimeo). Есть **Global overlay** — spotlight-подобное плавающее окно, Always on Top по умолчанию. С v11 можно назначить **правый Option**.

ASR полностью локально: whisper.cpp, WhisperKit, **Parakeet v2/v3 до 300x realtime**. Опционально облако: OpenAI, ElevenLabs Scribe, Deepgram Nova, Groq, Gladia. **AI-постобработка через 9 провайдеров, включая полностью локальные Ollama и LM Studio** — единственный продукт с честной цепочкой «всё локально от звука до готового текста». App Specific Prompts.

Главная претензия **[конкурентный источник]**: живая диктовка медленная (>2 400 мс), нет стриминга — это файловый инструмент ([lumevoice](https://lumevoice.com/blog/macwhisper-review-2026/)).

## 7. Whisper Notes — ценовой якорь

**$6.99 разово на платформу**, iPhone 12+/iOS 18+ и Apple Silicon Mac ([whispernotes.app](https://whispernotes.app/)). Скачивать **DMG с сайта** — версия в App Store не обновляется, потому что Apple потребовала убрать Accessibility, что сломало Fn-диктовку.

Активация: **удержание Fn** — классический push-to-talk. Локально: **Parakeet V3** (дефолт, 25 языков), Whisper Large V3 Turbo, SenseVoice. 35-минутный файл за 18 секунд на M4 Pro, WER 6.32%. **Real-time нет осознанно** — обработка после записи ради точности. Локальные саммари и чат с транскриптом. Нет iCloud-синхронизации — как заявленная позиция приватности. «We don't even know how many daily active users we have.»

---

# III. OPEN SOURCE

## 8. Handy — лучший референс UI-оверлея в отрасли

MIT, **27.2k★**, Win/macOS/Linux, v0.9.4 (21 июля 2026), Tauri 2 + Rust. [github.com/cjpais/Handy](https://github.com/cjpais/Handy)

Исходники оверлея прочитаны целиком (`overlay.rs`, `RecordingOverlay.tsx`, `RecordingOverlay.css`) — это самый ценный материал всего исследования.

**Геометрия.** Одно нативное окно под все состояния. Два форм-фактора:
- **Compact pill**: высота 40px, `border-radius: 24px`, ширина покоя 172px → рабочая 216px; окно 256×46
- **Live panel** (стриминг): ширина 392px, `border-radius: 16px`, окно 400×120

Переход между ними — **анимируемый морфинг ширины и радиуса, а не подмена компонента**.

**Состояния**: `recording` | `streaming` | `transcribing` | `processing`.

**Раскладка — одна 3-зонная сетка для всех состояний**, чтобы центр не «прыгал»:
```css
grid-template-columns: minmax(22px,1fr) auto minmax(22px,1fr)
```
- слева: пульсирующая точка 7×7px (`sdot-pulse` 1.9 с, расходящийся box-shadow до 7px) — **или** спиннер 13px (`sspin` 0.7 с linear) в рабочем состоянии
- центр: **waveform** — или метка «Transcribing…» / «Processing…»
- справа: таймер `mm:ss` с `font-variant-numeric: tabular-nums` + круглая кнопка отмены 22×22 (hover `scale(1.05)`, active `scale(0.95)`)

**Waveform — 9 полосок.** Бэкенд шлёт 16 FFT-бакетов, фронт сглаживает экспоненциально (`prev*0.7 + target*0.3`) и берёт первые 9. Высота: `max(3, min(18, 3 + pow(v, 0.7) * 15))` px — **гамма-коррекция 0.7 ради «живости» на тихих сигналах**. Полоски 4px, `border-radius: 2px`, `transition: height 80ms linear`.

**Живой транскрипт**: `committed` + `tentative` текст + мигающая каретка 2px (`steps(1)`, 1.05 с), курсив 15px. Область 64px со скрытым скроллбаром — вместо него **маска-градиент** `linear-gradient(to bottom, transparent 0, #000 18px)`, старые строки растворяются под пилюлей. **Scroll-pin**: авто-follow к низу, отключается если пользователь проскроллил вверх, порог 16px.

**Анимации**: появление `scard-pop 460ms cubic-bezier(0.22, 1, 0.36, 1)`, scale 0.92→1 от прижатого края. Уход: opacity 240 мс + scale 300 мс, Rust ждёт 300 мс перед `hide()`.

**Позиционирование**: Top / Bottom / None; появляется **на том мониторе, где курсор мыши**. На macOS нижняя позиция считается от work_area (следит за Dock).

**Нативная реализация — по-разному на трёх ОС:**
- **macOS**: не окно, а **NSPanel** через `tauri-nspanel` — `PanelLevel::Status`, `borderless().nonactivating_panel()`, `no_activate(true)`, `can_become_key_window: false`, `can_join_all_spaces().full_screen_auxiliary()`
- **Windows**: `decorations(false).always_on_top(true).skip_taskbar(true).transparent(true).focusable(false)` + принудительный `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)`, потому что штатный Tauri-шный always-on-top перебивается. Позиция **переустанавливается ещё раз после `show()`** — `WM_DPICHANGED` в tao затирает первую установку на мульти-DPI
- **Linux**: `gtk-layer-shell`, `Layer::Overlay`. **По умолчанию оверлей на Linux выключен**, потому что некоторые композиторы считают его активным окном, он крадёт фокус и вставка уходит не туда

**Производственный урок**: событие `mic-level` при 24 Гц вызывало неограниченный рост памяти WebKitWebProcess на Linux ([issue #1279](https://github.com/cjpais/Handy/issues/1279)). Починили троттлингом до ~30 FPS, пропуском эмита при выключенном оверлее и адресным `emit_to` вместо broadcast ([PR #1447](https://github.com/cjpais/Handy/pull/1447)).

**ASR**: собственные `transcribe.cpp` и `transcribe-rs`. Модели: Parakeet V2/V3, Whisper, Moonshine, Canary, SenseVoice и — важно для России — **GigaAM v3 (~225 МБ, русский, с пунктуацией и цифрами)**. GPU только для Whisper (Metal/Vulkan), остальное CPU.

Push-to-talk **по умолчанию**, Option+Space / Ctrl+Space. Стриминг с v0.9.0. **В проекте объявлен feature freeze** — приоритет багфиксам.

## 9. Остальные OSS

- **Buzz** (19.7k★, MIT) — **не диктовщик**: нет глобального хоткея, нет вставки в чужие приложения. Рабочее место транскрипции. Ценен параметрами live-режима: *Transcription step*, *Hide unconfirmed* (скрывать неподтверждённую часть — больше задержки, но текст не «дёргается»), режим `Append and correct`, отдельное **Presentation Window** для живых субтитров
- **OpenWhispr** (4.7k★, MIT, Electron) — плавающая перетаскиваемая панель. **Tap-to-talk по умолчанию, PTT только на Windows** через нативный `windows-key-listener.exe`. Дефолты: Globe/Fn на macOS, Ctrl+Win на Windows. Лучшая на рынке вставка на Windows (см. раздел V)
- **Whispering** (4.7k★, AGPL) — **вообще нет оверлея, и это его главная жалоба**. Мета-трекер [issue #848](https://github.com/EpicenterHQ/epicenter/issues/848) собирает запросы на трей, меню-бар и «Better visualization for when Whispering is Recording». На HN пользователь d4rkp4ttern: *«VoiceInk is far superior to Whispering in terms of UX… show a visual icon with waves etc showing recording»* ([news.ycombinator.com](https://news.ycombinator.com/item?id=44942731)). Плюс macOS App Nap гасит глобальные шорткаты в фоне
- **WhisperWriter** (1.1k★) — заброшен (последний релиз январь 2024), но ценен **четырьмя режимами записи**: `continuous`, `voice_activity_detection`, `press_to_toggle`, `hold_to_record`. Показывает «small status window» со стадией процесса
- **Vibe** (6.7k★, MIT, Tauri) — **единственный из «старой гвардии» с явно задокументированным real-time audio visualizer** (v3.0.11). CLI + HTTP API со Swagger
- **whisper-local** (0★, но идеи стоят внимания): **pre-roll буфер 500 мс** + прогрев модели при старте («первое слово никогда не срезается»); **fallback-окно**, если фокуса нет ни в одном текстовом поле; **per-app правила** с `suppress: true` для 1Password/Bitwarden

---

# IV. ЛЕГАСИ И ВСТРОЕННОЕ

## Windows: главный архитектурный разлом

**Win+H (Voice Typing)** — **облачный** (Azure), требует интернета ([Microsoft](https://support.microsoft.com/en-us/windows/speech-voice-activation-inking-typing-and-privacy-149e0e60-7c93-dedd-a0d8-5731b71a4fef)). Плавающая перетаскиваемая панель: круглая кнопка микрофона, шестерёнка, текстовый статус («Listening…» / «To use voice typing, select a text box»). **Waveform нет.** Автозакрытие после ~30 с тишины. Опция «voice typing launcher» — панель сама появляется при клике в поле.

**Voice Access** — **локальный, работает офлайн** ([Microsoft](https://support.microsoft.com/en-US/accessibility/windows/voice-access/use-voice-access-to-control-your-pc-author-text-with-your-voice)). Панель **пристыкована к верхнему краю**: слева — транскрипция в реальном времени («Refer to this to know what voice access heard» — то есть панель как инструмент отладки), по центру — статус выполнения команды, кнопка микрофона, шестерёнка, помощь. Три состояния: Listening / Sleep / Microphone off. Wake word **«Voice access wake up»**, хоткей Alt+Shift+B. **Show numbers** и рекурсивная **числовая сетка 1–9**.

Что две функции одной ОС имеют противоположную архитектуру приватности — и пользователи об этом не знают — это документированный факт, а не интерпретация.

**Новое в 2026**: Fluid Dictation на Copilot+ PC (локальные SLM расставляют пунктуацию и убирают «эм», авто-отключается в полях паролей); отключаемый фильтр мата; **Voice Isolation** (июль 2026, отсечение других говорящих); итальянский в Voice Access.

**Главный дефект Voice Access по отзывам**: добавление слов в словарь улучшает **только список предложений при коррекции, но не первичное распознавание** ([learn.microsoft.com](https://learn.microsoft.com/en-us/answers/questions/5790297/problems-with-voice-access-for-windows-11-consiste)).

## macOS Dictation

**Нет плавающей панели вообще.** Индикация: **курсор подсвечивается и пульсирует** + звуковой тон. Неоднозначные слова **подчёркиваются синим**, клик открывает альтернативы. Активация — **двойное Fn** или клавиша микрофона (короткое нажатие = диктовка, удержание = Siri). Автостоп через 30 с. На Apple Silicon можно **печатать одновременно с диктовкой** — курсор перестаёт пульсировать во время набора ([Apple](https://support.apple.com/guide/mac-help/use-dictation-mh40584/mac)).

## Dragon — угасающий актив

Dragon Professional v16 — **$699.99 бессрочно, только Windows**, последний мажор 2023. **Dragon Anywhere Mobile снят с продаж 1 июля 2026.** Dragon Home закрыт в 2023, Dragon для Mac — в 2018. Microsoft перенесла бренд целиком в здравоохранение (Dragon Copilot: 58 языков, 100 000+ клиницистов, HIMSS 2026).

UI: **DragonBar** — узкая закрепляемая панель, **зелёный микрофон = включён, красный = выключен**; **Dictation Box** для приложений без Full Text Control; **Results Box** с промежуточным результатом у точки ввода. Аппаратный push-to-talk через PowerMic 4 и ножную педаль.

Неперекрытым остаётся слой Dragon: **адаптация акустической модели под конкретного пользователя, макросы, централизованное управление словарями**.

## Talon Voice — другая категория

Бесплатно (Patreon от $5), Win/macOS/**Linux только X11** (Wayland не поддерживается и не будет). **Это не диктовка, а hands-free управление компьютером.** Из коробки не реагирует на речь вообще — нужны скрипты.

**UI почти нет, и это осознанно**: иконка в трее (форма = индикатор сна), экранные субтитры произнесённых фраз, imgui-окна. Waveform в штате нет. Сообщество добавляет **Talon HUD** со статус-баром, микрофонным тогглом и **focus indicator** (оранжево-красный прямоугольник по верху сфокусированного окна).

Три режима вместо PTT: **Command / Dictation / Sleep**. Speech timeout 0.3 с. **Noise commands**: pop, hiss (в бете — Parrot: cluck, tut, palate_click, gluck, finger_snap + вокальные ah, oh, ee). Eye tracking (Tobii 4C/5), Mouse Grid.

ASR полностью локальный: wav2letter → Conformer b108 → **Conformer D** (0.4.0, ~20% точнее). Ядро переписано на **Rust**. В бете — гибридный движок на Whisper и «Mixed Mode».

**Cursorless** — отдельная экосистема поверх Talon для VS Code: «hats» (цветные/фигурные метки над символами), синтаксические скоупы (`funk`, `class`, `arg`, `lambda`…), действия (`chuck`, `carve`, `bring`, `swap X with Y`). Фонетический алфавит оптимизирован под слоги: air, bat, cap, drum, each…

Главная жалоба: *«It's easy to lose your patience when the engine repeatedly misidentifies a command you are trying to give, and inadvertently deletes a whole chunk of your document instead»*, и ловушка — в command mode Talon не «ослышался», а **подобрал ближайшую команду из словаря** ([fileside.app](https://www.fileside.app/blog/2025-04-14_voice-computing/)).

## Otter / Notta — не конкуренты

Обе — **meeting-транскрипция**, системного хоткея для диктовки нет. Otter: Free / $8.33 / $19.99 годовых; главная тема 2026 — **консолидированный федеральный коллективный иск** о записи без согласия участников, а не качество. Notta: Free / $8.17 / $16.67 годовых + платные аддоны; главная тема — **биллинг**: G2 4.6/5 против Trustpilot ~1.8/5, жалобы на списания после отмены триала.

---

# V. РУССКОЯЗЫЧНЫЙ РЫНОК

**Ниша уже не пуста, но рынок очень молодой и слабый по исполнению.** С конца 2025 по середину 2026 вышло минимум 5–6 клонов модели Wispr Flow:

| Продукт | Платформы | Цена | Модель |
|---|---|---|---|
| **Поток** ([potok.now](https://potok.now/)) | Win, macOS, **Linux** | Free 2500 слов/нед; Pro **690 ₽/мес** (500 ₽ год) | «Своя модель», облако, **серверы в РФ**, 152-ФЗ, on-prem |
| **Диктуй** ([diktuy.ru](https://diktuy.ru/)) | Win 10/11, macOS 11+ | Free **30 мин/мес навсегда**; Pro 449 ₽, Unlimited 599 ₽ | **Whisper** (заявлено прямо) |
| **SpeakFlow** ([speakflow.ru](https://speakflow.ru/)) | Win 10/11 | Free 30–50 мин; Персональный **690 ₽/мес** | Не раскрывается |
| **VoiceBoard** ([vb.intelforce.ru](https://vb.intelforce.ru/)) | Win 10/11 | Pro 290 ₽, Ultra 490 ₽ | **4 движка на выбор**, включая **Vosk офлайн** и свой Whisper-сервер |
| **Vox** ([getvox.ru](https://getvox.ru/)) | macOS, Win | Прайс получить не удалось | Whisper |

UI-паттерн у всех одинаковый: хоткей (Ctrl+Space у Потока и Диктуя, Ctrl+Win у SpeakFlow) → всплывающий индикатор → вставка по курсору + словарь + сниппеты + история.

**Все заявленные метрики точности — самозаявленные и несопоставимые** (Поток: WER 2,1% на GolosTest; Диктуй: 95–98%; SpeakFlow: 96–98% и «топ-1 на рынке» в собственной сравнительной статье). Независимой проверки нет. У Потока отзывы на сайте помечены как **«собирательные, не реальные люди»**.

**Системная проблема кластера — отсутствие цифровой подписи кода.** Все — ИП/самозанятые с инструкциями «SmartScreen → Подробнее → Выполнить в любом случае».

Единственный содержательный независимый разбор — [habr.com/ru/articles/1024634](https://habr.com/ru/articles/1024634/) (апрель 2026): про SpeakFlow — *«memory leaks, подвисания. Некоторые апдейты всё ломали»*, плюс деанонимизация при обращении в поддержку через личный Telegram.

### Технический задел, который никто не использует

**GigaAM-v3** от Сбера (декабрь 2025, **лицензия MIT**, 220M параметров, работает на CPU) — [github.com/salute-developers/GigaAM](https://github.com/salute-developers/GigaAM):

| Датасет | GigaAM v3 RNNT | T-One + LM | Whisper large-v3 |
|---|---|---|---|
| Golos Farfield | **3,9** | 12,2 | 16,4 |
| Golos Crowd | **2,4** | 5,7 | 19,0 |
| Common Voice 19 | **0,9** | 5,2 | 5,4 |
| Callcenter | **9,5** | 13,5 | 23,1 |
| **Среднее** | **6,7** | 15,5 | **20,8** |

Ни один российский продукт диктовки её не использует. Handy — **иностранный OSS** — поддерживает GigaAM v3 из коробки.

**Известная слабость GigaAM — code-switching**: «Gemini → Jemni», «Whisper Large → WisperLorge», растяжки «аааа» прямо в текст. Это и есть главная боль русскоязычных вайб-кодеров.

**Экономика API** (июль 2026): T-Bank VoiceKit — **0,48–0,72 ₽/мин** ([developer.tbank.ru](https://developer.tbank.ru/voicekit/intro/tariff)); Яндекс SpeechKit — **0,65 ₽/мин** потоково ([aistudio.yandex.ru](https://aistudio.yandex.ru/docs/ru/speechkit/pricing.html)). **Сбер с 15 июля 2026 прекратил продажу SaluteSpeech физлицам** ([developers.sber.ru](https://developers.sber.ru/docs/ru/salutespeech/tariffs/individual-tariffs)) — уходит из B2C и освобождает поле.

SaluteSpeech App существует (Windows + macOS, v2.3.1), но **это не диктовка** — загрузка файлов + синтез.

---

# ОТВЕТЫ НА ОТДЕЛЬНЫЕ ВОПРОСЫ

## 1. UI/UX-паттерны, ставшие де-факто стандартом в 2026

**Стало обязательным:**

1. **Push-to-talk удержанием одной клавиши как дефолт.** Fn на macOS (Wispr Flow, Aqua, Willow, Whisper Notes), Ctrl+Win или Alt на Windows. Обратите внимание: **встроенные средства ОС этого не дают вообще** — ни Win+H, ни Voice Access, ни macOS Dictation не имеют настоящего PTT, только toggle. Это и есть главный водораздел между «системной» и «продуктовой» диктовкой.

2. **Двойной тап того же хоткея = переход в hands-free.** Wispr Flow, Willow. Один жест — два режима, ничего не нужно запоминать.

3. **Плавающий pill/bar, прижатый к краю экрана**, а не диалог по центру. Handy, Aqua, Willow, Wispr Flow, VoiceInk (mini). **Aqua и Wispr Flow оба используют слово «pill»/«bar» в официальных материалах.**

4. **Waveform как индикатор записи.** Есть у: Handy (9 полосок), Superwhisper (main + mini), Wispr Flow («white bars», Android — «pulsing waveform»), Vibe. Нет у: Win+H, Voice Access, macOS Dictation, Dragon (только volume meter), Talon.

5. **Явно различимые визуальные состояния для «записываю» и «распознаю».** Superwhisper кодирует цветом точки (жёлтый/красный/синий/зелёный), Handy — заменой waveform на спиннер и текстовую метку.

6. **Esc отменяет.** Универсально. Wispr Flow и Handy пошли дальше — Esc перебиндивается (конфликтует с Vim и терминалами).

7. **Автоматическое сохранение и восстановление буфера обмена.** Стало гигиеническим минимумом.

8. **Персональный словарь + сниппеты/замены.** Есть у всех платных без исключения.

9. **Автопереключение конфигурации по активному приложению.** VoiceInk Power Modes, Superwhisper auto-activation rules, MacWhisper App Specific Prompts, Wispr Flow Styles, Willow context awareness.

10. **История диктовок с возможностью retry.** Wispr Flow сделал retry «одной из самых частых просьб поддержки».

**Ещё не стандарт, но явный тренд:**
- **Streaming-вывод текста в реальном времени** — Aqua (Streaming Mode), Handy (v0.9.0), Buzz. У Wispr Flow и Willow **нет** — текст вставляется целиком
- **Голосовое редактирование выделенного текста** — Aqua Edit Mode, Wispr Flow Command Mode
- Мышиные кнопки как триггер — Wispr Flow Mouse Flow, Superwhisper Mouse Shortcut

## 2. Лучшие анимационные и визуальные приёмы

Ранжирую по доказуемой ценности. Всё, кроме п.6–8, — из исходников Handy, единственного полностью читаемого источника.

1. **Морфинг одного окна между состояниями вместо смены компонентов.** Handy: pill 172px → 216px → panel 392px, `border-radius` 24px → 16px, всё на `cubic-bezier(0.22, 1, 0.36, 1)` за 460 мс. Пользователь видит **один непрерывный объект**, а не мигающие окна.

2. **Фиксированная 3-зонная сетка с `auto` в центре.** Центральный элемент (waveform ↔ «Transcribing…») меняется, но не сдвигает соседей. Это устраняет самый раздражающий класс глитчей — «прыгающий UI» (Aqua отдельно чинила «The microphone UI no longer jumps around when recording» в январе 2026).

3. **Гамма-коррекция амплитуды waveform.** `3 + pow(v, 0.7) * 15` — линейная амплитуда выглядит вяло на тихой речи. Экспонента 0.7 делает столбики «живыми». Плюс экспоненциальное сглаживание `prev*0.7 + target*0.3` и `transition: height 80ms linear`, чтобы не дрожало.

4. **Анимация появления от прижатого края.** `scale(0.92) → 1` с `transform-origin` у края экрана. Панель «выдавливается» из края, а не появляется в пустоте и никогда не выезжает за границу.

5. **Маска-градиент вместо скроллбара для живого текста.** `linear-gradient(to bottom, transparent 0, #000 18px)` — старые строки растворяются под пилюлей. Плюс **scroll-pin**: авто-follow к низу, но отключается, если пользователь читает историю.

6. **Разделение committed / tentative текста + мигающая каретка** `steps(1)`. Buzz формализует это настройкой *Hide unconfirmed* — прямо признавая компромисс: скрывать неподтверждённое = больше задержки, но текст не «дёргается».

7. **Цветовой код состояния, продублированный в нескольких местах.** Superwhisper: одна и та же семантика (жёлтый/красный/синий/зелёный) в меню-баре, main window и mini window.

8. **Контекстный чип над панелью, объясняющий, что сейчас произойдёт.** Aqua Edit Mode: «12 words selected». Снимает главную тревогу голосового интерфейса — «а не сотрёт ли оно мне текст».

9. **Разворачивание пузыря в панель с тремя контролами** (Cancel — waveform — Done). Android Flow Bubble, Superwhisper mini на hover.

10. **Постепенная деградация присутствия.** Android Flow Bubble: авто-сжатие через 5 с → компактная иконка → «крошечная точка»; слайдер прозрачности 20–100%; авто-минимизация в полях поиска.

11. **Звуковая обратная связь наравне с визуальной.** Документация Wispr Flow: «When you hear the ping **or** see the white bars moving». Звук назван первым — он не требует перевода взгляда.

**Отдельно, о чём почти никто не думает:** событие уровня микрофона нужно **троттлить до ~30 FPS** и **вообще не эмитить, если оверлей выключен**. Иначе — неограниченный рост памяти рендерера (реальный баг Handy на Linux, [#1279](https://github.com/cjpais/Handy/issues/1279)).

## 3. Что делает Wispr Flow, чего не делают остальные

**Уникально:**

1. **Четырёхуровневый Auto Cleanup** (None / Light / Medium / High) с **обратимостью** — «Undo AI edit» в истории возвращает сырой транскрипт. У всех остальных обработка либо есть, либо нет.

2. **Изменение настроек голосом с подтверждением.** «I don't like to use the word utilize» → уведомление с кнопкой Apply → сохраняется в Polish-настройки. Никто больше не делает голосовую конфигурацию продукта.

3. **Recall по собственной истории.** «Можешь напомнить, что я диктовал про Х» → Flow ищет по прошлым диктовкам, заметкам и встречам и вставляет inline. Читает календарь и напоминания (создавать не может).

4. **Mouse Flow.** Полноценная привязка к кнопкам мыши Mouse4–Mouse10 с гайдом, плюс отдельная привязка Enter на кнопку мыши.

5. **Единственный по-настоящему кросс-платформенный**: Mac + Windows + iPhone + Android, с синхронизацией словаря, стиля и настроек. Aqua — без Android, Superwhisper — без Android, Willow — Android «скоро», VoiceInk — только Apple.

6. **Ранжирование микрофонов с автопереключением** при подключении гарнитуры и в clamshell-режиме, без прерывания диктовки.

7. **Scratchpad** — блокнот поверх всего по Option+S с rich-text, вкладками и историей версий.

8. **Insights / геймификация** — WPM против других пользователей, стрик-хитмап, «communication archetype», лидерборд для команд.

9. **Автопауза в 50+ банковских приложениях** (Android) без настройки.

10. **Радикальная публичная прозрачность про сбои.** Ни один конкурент не публикует «вот что мы сломали и почему» с такой детализацией. Это одновременно и сильная, и слабая сторона: доверие растёт, но количество инцидентов становится публичным фактом.

**Чего у Flow, наоборот, нет, а у конкурентов есть:**
- **Локального режима вообще** (есть у Superwhisper, VoiceInk, MacWhisper, Whisper Notes, Handy, у Willow — fallback)
- **Streaming-вывода** (есть у Aqua, Handy)
- **Опубликованных бенчмарков** (Aqua публикует Open ASR Leaderboard и AISpeak)
- **Разовой покупки** (VoiceInk $25, MacWhisper €59, Whisper Notes $6.99, Superwhisper $249.99 lifetime)
- **Бесплатного безлимита** — Willow с июля 2026 даёт его, Flow держит 2 000 слов/нед

## 4. Какие фичи пользователи просят чаще всего

По убыванию частоты упоминаний в issues, HN, обзорах и заявлениях самих вендоров:

1. **Офлайн / локальная обработка.** Абсолютный лидер. Для Aqua это претензия №1 в рецензии 9to5Mac. Для регулируемых отраслей формулируется как жёсткое требование, а не пожелание. Aqua честно пишет: «users repeatedly ask for iOS, mobile, Linux, and offline support».

2. **Разовая оплата вместо подписки.** VoiceInk продаёт себя буквально этим («This app is so good I have been using wispr flow at $12 a month… It's basically a one off payment»). Whisper Notes ставит якорь $6.99.

3. **Стабильность важнее фич.** Одна и та же жалоба на все продукты без исключения — включая лидера: пропадающая пунктуация, утечки памяти, подвисания при фоновой работе, «некоторые апдейты всё ломали». У Handy это оформлено в **feature freeze**: новые фичи отклоняются, приоритет — 60+ багов.

4. **Плавающий индикатор записи с waveform.** Главная жалоба на Whispering ([#624](https://github.com/EpicenterHQ/epicenter/issues/624), [#848](https://github.com/EpicenterHQ/epicenter/issues/848)). Формулировка с HN: *«show a visual icon with waves etc showing recording»*.

5. **Не терять первые слова.** У Wispr Flow это официальная статья в Known Issues. У Handy — «первые несколько слов каждой транскрипции обычно теряются». Решение известно и никем массово не внедрено: **pre-roll буфер + прогрев модели**.

6. **Голосовая пунктуация и команды форматирования** — [Discussion #662](https://github.com/cjpais/Handy/discussions/662) в Handy.

7. **Прозрачная работа с буфером обмена.** Цитата с HN: *«how the clipboard is handled during recording (does it copy to clipboard? does it clear it after text output?)»*.

8. **Гибкие хоткеи**: modifier-only, right-shift как toggle, гибрид «короткое нажатие = toggle, длинное >0.5 с = hold-and-release», несколько биндов на одно действие.

9. **Code-switching** — вкрапления английского в неанглийскую речь. Для русскоязычных это боль №1: *«Было бы супер иметь модель, заточенную под русский, которая при этом хорошо понимает вкрапления английского — ждём»*.

10. **Диаризация + суммаризация** — [Discussion #599](https://github.com/cjpais/Handy/discussions/599).

11. **Linux.** Полностью отсутствует у всех коммерческих лидеров. Из российских — только Поток.

12. **Транскрипция файлов в дикт-приложении** — [issue #1494](https://github.com/cjpais/Handy/issues/1494).

## 5. Как продукты решают проблему вставки текста в чужие приложения

Это самая техничная часть, и здесь есть чему поучиться у OSS, потому что коммерческие продукты свои решения не документируют.

### Доминирующий подход: буфер обмена + синтетическая комбинация клавиш

Так делают **все**, включая Wispr Flow: *«Flow temporarily uses your clipboard to paste text, but automatically saves and restores your previous clipboard contents afterward»* ([docs.wisprflow.ai](https://docs.wisprflow.ai/articles/6409258247-starting-your-first-dictation)).

**Критический приём: отправлять виртуальные коды клавиш, а не символы.** Из `clipboard.rs` Handy с прямым комментарием в коде:
```rust
#[cfg(target_os = "macos")]   let (m, v) = (Key::Meta,    Key::Other(9));    // физическая V
#[cfg(target_os = "windows")] let (m, v) = (Key::Control, Key::Other(0x56)); // VK_V
// "This ensures the paste works regardless of keyboard layout (e.g., Russian, AZERTY, DVORAK)"
```
Если отправить символ `'v'`, на русской, AZERTY или QWERTZ раскладке нажмётся другая клавиша. Что это не теоретическая проблема, доказывает Known Issue самого Wispr Flow: **«Flow fails to detect text fields or inserts incorrectly on non-QWERTY keyboard layouts»**.

### Тайминги — источник половины багов

Handy: записать в буфер → `sleep(60 мс)` (настраивается, доки советуют 60–200) → комбинация → `sleep(50 мс)` → восстановить буфер.
VoiceInk: `prePasteDelay 0.10 с` → Cmd+V → восстановление **не раньше 0.25 с**.

Слишком рано нажать Ctrl+V → вставится предыдущее содержимое буфера. Слишком рано восстановить → затрётся то, что не успело вставиться.

### Лучшее решение проблемы «потерянного буфера» — VoiceInk

`CursorPaster.swift` + `PasteMethod.swift`:
- снимок **всех** `NSPasteboardItem` со **всеми** типами данных, не только строки
- **session-ID guard**: перед восстановлением проверяется, что в пастборде всё ещё лежит именно наш текст **и** служебный тип `ClipboardManager.pasteSessionType` с UUID сессии. Если пользователь успел скопировать своё — восстановление **не выполняется**

Это ровно тот класс багов, на который жалуются во всех остальных продуктах.

### macOS: три пути и их ловушки

**(а) CGEvent + буфер** (дефолт VoiceInk):
```swift
guard AXIsProcessTrusted() else { return .commandNotPosted }
let source = CGEventSource(stateID: .privateState)  // чтобы не менять активный input source
// virtualKey 0x37 = Command, 0x09 = V, +10 мс между событиями
cmdDown.post(tap: .cghidEventTap)
```

**(б) AppleScript** `keystroke "v" using command down` — с нетривиальным фиксом: у раскладок вида «X – QWERTY ⌘» при зажатом Command клавиатура переключается на QWERTY и `keystroke "v"` резолвится не в ту клавишу. VoiceInk детектирует такие раскладки по суффиксу «⌘» в `kTISPropertyLocalizedName` и шлёт `key code 9`.

**(в) Accessibility API напрямую** (`AXUIElementSetAttributeValue` + `kAXSelectedTextAttribute`) — формально «правильный» путь, но **не возвращает ошибку и при этом молча не вставляет** на многих типах элементов, особенно в WebView и Electron. **Ни один из рассмотренных проектов не использует его как основной.**

**Ловушки macOS:**
- **App Sandbox несовместим**: при включённом сэндбоксе промпт Accessibility никогда не появляется, `AXIsProcessTrusted()` всегда `false`
- **Mac App Store отклоняет `CGEvent.post`** по Guideline 2.4.5 — именно поэтому MacWhisper и Whisper Notes раздают DMG с сайта, а версия в App Store лишена диктовки
- **Secure Input** (Terminal «Secure Keyboard Entry», 1Password, KeePassXC, зависший loginwindow) блокирует доставку событий. Handy детектирует это, показывает баннер с именем виновного процесса и автоматически переключает шорткат на фолбэк
- **Протухшее TCC-разрешение** после обновления подписи. И Handy, и OpenWhispr документируют один ритуал: удалить приложение из Accessibility и добавить заново
- **App Nap** усыпляет фон и убивает глобальные шорткаты (проблема Whispering)
- **Оверлей не должен активироваться.** NSPanel обязан быть `nonactivatingPanel` + `canBecomeKeyWindow: false`. Иначе он сам становится фокусным окном и Cmd+V уходит в него

### Windows: пять проблем

1. **UIPI.** `SendInput` инжектит ввод только в приложения равного или меньшего integrity level, и **при блокировке не сообщает об ошибке** — ни возвращаемое значение, ни `GetLastError` ([MS Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)). Единственное решение у всех: «запускайте приложение тоже от админа».

2. **Консоли не берут Ctrl+V.** Два подхода: Handy предлагает выбрать **Shift+Insert** вручную; **OpenWhispr автоматически определяет foreground-окно** и для терминалов шлёт Ctrl+Shift+V — детектируемый список: Windows Terminal, cmd, PowerShell, mintty, PuTTY, Alacritty, WezTerm, kitty, Hyper, MobaXterm, ConEmu. Это лучшее решение на рынке.

3. **Раскладка** — см. выше про виртуальные коды.

4. **Гонка буфера** — см. тайминги.

5. **Оверлей поверх всех и без фокуса**: `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)`, `focusable(false)`, `skip_taskbar(true)`. Handy отдельно отмечает, что штатный Tauri-шный `set_always_on_top` перебивается, и что позицию надо переустанавливать **после** `show()` из-за реакции tao на `WM_DPICHANGED`.

### Electron-специфика
- **`robotjs` работает в dev и не работает в production на macOS** ([issue #535](https://github.com/octalmage/robotjs/issues/535))
- **Electron global shortcuts вообще не работают на Wayland**
- Поэтому OpenWhispr отказался от JS-библиотек и **поставляет собственные нативные бинарники**: `windows-fast-paste`, `linux-fast-paste`, `windows-key-listener.exe`, Swift/ObjC-listener для Globe на macOS

### Альтернатива без буфера: посимвольный ввод
`enigo.text()` (Handy «Direct Input»), `pynput` с `writing_key_press_delay: 0.005` (WhisperWriter). Минусы очевидны и признаются в документации Handy: «Direct input does not account for non-US keyboard layouts… the output will be garbled», плюс медленно на длинных текстах и ломается об автодополнение в редакторах.

### Матрица методов, которую стоит копировать
Handy предлагает **шесть** ([handy.computer/docs/paste-methods](https://handy.computer/docs/paste-methods)): Cmd/Ctrl+V, Ctrl+Shift+V, Shift+Insert, Direct Input, None (только буфер), External Script (Linux). Плюс `Auto Submit` — Enter / Cmd+Enter / Ctrl+Enter через 50 мс после вставки.

### Незакрытые сценарии
- **Что делать, если фокуса нет ни в одном текстовом поле.** Решает только whisper-local — показывает fallback-окно с уже выделенным текстом и кнопкой копирования. Все остальные молча теряют текст либо вставляют не туда
- **Не вставлять в менеджеры паролей.** Тоже только whisper-local (`suppress: true` для 1Password/Bitwarden). Wispr Flow делает аналог только на Android и только для банков
- **Linux Wayland** — целая иерархия фолбэков (`kwtype` → `wtype` → `dotool` → `ydotool`), и главная беда: оверлей крадёт фокус, поэтому Handy **отключает оверлей на Linux по умолчанию**

---

# ГЛАВНЫЕ ВЫВОДЫ И НЕЗАКРЫТЫЕ НИШИ

1. **Категория консолидировалась вокруг одного взаимодействия**: удержал Fn → говорил → отпустил → готовый отформатированный текст по курсору. Различия сместились с «распознаёт ли» на «как обрабатывает» и «насколько надёжно вставляет».

2. **Wispr Flow лидирует по охвату и фичам, но платит за это надёжностью** — и, что необычно, признаёт это публично. Aqua лидирует по проверяемому качеству и латентности. Willow с июля 2026 бьёт по цене (бесплатный безлимит). Superwhisper — единственный с настоящим локальным режимом и осмысленной lifetime-лицензией.

3. **Ни один коммерческий лидер не поддерживает Linux.**

4. **Пересечение «полностью офлайн» × «хороший русский» пусто.** GigaAM v3 под MIT даёт 6,7% WER против 20,8% у Whisper large-v3 на русских доменах, работает на CPU, и её не использует ни один российский продукт диктовки.

5. **Push-to-talk остаётся структурным преимуществом над встроенными средствами ОС** — ни Win+H, ни Voice Access, ни macOS Dictation его не дают.

6. **Handy — готовый, вычищенный референс UI-оверлея под три ОС**, а VoiceInk — референс работы с буфером обмена. Оба читаемы целиком и оба решают задачи, которые коммерческие продукты решают хуже.

### Чего найти не удалось (не выдумано)
- Пиксельной геометрии и таймингов анимации у Wispr Flow, Aqua, Willow, MacWhisper — только видео и GIF
- Waveform у Willow — прямых подтверждений нет
- Абсолютной latency Wispr Flow (только «−30% с начала года»)
- Базовой ASR под edit-моделью Willow
- Первичных Reddit-тредов — домен недоступен для фетча
- Точной цены бета-тира Talon на Patreon (источники расходятся: $15 vs $25)
- Прайса Vox (getvox.ru отдаёт пустой ответ)
- Независимых замеров точности для Потока, Диктуя, SpeakFlow
agentId: aef298ef36e37d58a (use SendMessage with to: 'aef298ef36e37d58a', summary: '<5-10 word recap>' to continue this agent)
<usage>subagent_tokens: 282325
tool_uses: 36
duration_ms: 1493901</usage>
