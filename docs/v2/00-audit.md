# Аудит Egoist Voice 1.6.5

Дата: 26 июля 2026. Метод: полное чтение исходников (7 254 строки `.cs` + `.xaml`), артефактов
рендера в `artifacts/audit/1.6.2-baseline`, `artifacts/visual-1.5.0` и существующего
`docs/research.md`. Всё, что ниже, подтверждается конкретным местом в коде; там, где данных нет,
это сказано прямо.

Базовая линия на момент аудита: `dotnet build -c Release` — 0 ошибок, 0 предупреждений;
`dotnet test` — **101/101 пройдено**.

---

## 1. Что построено

```
Egoist.Voice (net8.0-windows, WPF + WinForms-трей, self-contained)
│
├── Вход
│   ├── MousePushToTalkService     WH_MOUSE_LL, Mouse 4/5
│   ├── GlobalHotkeyService        RegisterHotKey + опрос отпускания 16 мс
│   ├── PushToTalkCoordinator      объединение источников
│   ├── GameForegroundPolicy       19 имён процессов + 6 маркеров путей
│   └── ActivationSettingsService  activation.json, атомарная запись
│
├── Аудио
│   ├── AudioCaptureService        NAudio WaveInEvent (MME), 16 кГц/16 бит/моно, буфер 32 мс × 3
│   └── SpeechActivityDetector     энергетический гейт «была ли речь вообще»
│
├── ASR
│   ├── GigaAmTranscriptionService  sherpa-onnx, GigaAM v3 e2e-RNNT INT8, provider="cpu"
│   │   ├── GigaAmAudioChunker      нарезка >22 с по тишине
│   │   └── TranscriptChunkJoiner   склейка + дедупликация стыков
│   ├── WhisperTranscriptionService Whisper.net, large-v3-turbo-q5_0, WithLanguage("ru")
│   ├── HybridTranscriptionService  Task.WhenAll обоих + MixedLanguageTranscriptSelector
│   └── ModelManager                Range-докачка, SHA-256, marker-файлы
│
├── Текст
│   ├── TranscriptFormatter         абзацы (только для Whisper)
│   ├── TranscriptNormalizer        4 regex-правила
│   ├── TechnicalTermCatalog        44 зашитых термина
│   ├── ClipboardService            SetDataObject + до 6 ретраев
│   ├── TextInsertionService        SendInput → Ctrl+V
│   └── DictationDeliveryService    оркестрация, 3 статуса
│
└── UI
    ├── MainWindow                  242×72 окно, тело 218×48, AllowsTransparency
    ├── PixelPerfectCapsuleBorder   RenderTargetBitmap ×4 суперсэмплинг на каждый OnRender
    ├── CapsuleWaveformProfile      24 полосы, RMS+peak, двойное экспоненциальное сглаживание
    ├── CapsulePositionService      settings.json, Clamp по VirtualScreen
    ├── CapsuleHidePolicy           чистая функция «можно ли завершить скрытие»
    ├── TrayService                 ContextMenuStrip + EgoistTrayRenderer
    └── CustomShortcutDialog        захват пользовательской комбинации
```

**Что сделано по-настоящему хорошо** и что рефакторинг обязан сохранить:

- Чистая логика, вынесенная из UI и покрытая тестами: `CapsuleHidePolicy`,
  `PushToTalkCoordinator`, `CapsulePositionService.Clamp`, `CapsuleWaveformProfile`,
  `RecognitionProgressPolicy`, `MixedLanguageTranscriptSelector`.
- Атомарная запись настроек через `.tmp` + `File.Move(overwrite: true)` в обоих сервисах.
- Транзакционная смена биндинга активации с откатом при отказе Windows
  (`MainWindow.xaml.cs:179-297`): новые хуки создаются **до** снятия старых.
- Хук мыши никогда не подавляет событие — `return CallNextHookEx(...)` безусловно
  (`MousePushToTalkService.cs:73`). Игровые бинды не ломаются.
- Уровень звука не маршалится через диспетчер — только `volatile float`.
- `IsReducedMotion` и ветка `HighContrast` реализованы честно, а не для галочки.
- QA-инфраструктура рендера состояний (`--render-state-preview`, `EgoistTrayVisualPreview`) —
  редкость для WPF-проектов и прямая опора для визуальных регрессий в v2.
- Фокус не крадётся: `ShowActivated=False` + `Focusable=False` + `WS_EX_NOACTIVATE` +
  `SW_SHOWNOACTIVATE`, с логированием до/после.

Это не переписывание с нуля. Это перестройка трёх слоёв поверх здорового скелета.

---

## 2. Критические дефекты

### 2.1 🔴 Декод GigaAM исполняется на UI-потоке

`ConfigureAwait(false)` в проекте отсутствует полностью (проверено grep по всему дереву).
Цепочка: `StopAndTranscribeAsync` стартует на UI-потоке → `await _audioCapture.StopAsync`
(`MainWindow.xaml.cs:496`) возвращает продолжение в Dispatcher →
`HybridTranscriptionService.TranscribeAsync` идёт синхронно (тёплые `WarmUpAsync` и `WaitAsync`
уже завершены) → `GigaAmTranscriptionService.cs:78` `await Task.Run(...)` — единственный
настоящий await, **его продолжение постится обратно в Dispatcher** → `await
_decodeLock.WaitAsync` (:80) при свободном семафоре завершается синхронно → **цикл :85-99 с
блокирующими нативными `_recognizer.Decode(stream)` (:94) выполняется на UI-потоке**.

Последствия, по возрастанию тяжести:

1. Анимация «Распознаю» замирает — капсула не перерисовывается.
2. `Progress<ModelProgress>` (`MainWindow.xaml.cs:510`) постит в тот же заблокированный
   Dispatcher — прогресс не отображается.
3. **WH_MOUSE_LL диспетчеризуется в очередь этого же потока.** При декоде дольше
   `LowLevelHooksTimeout` (по умолчанию 300 мс) Windows пропускает вызов хука, а в ряде версий
   **снимает его молча**. То есть Mouse 5 может перестать работать после длинной диктовки, и
   приложение об этом не узнает — переустановки хука в коде нет.
4. Пока хук не вернулся, подтормаживает **вся системная мышь**.

Это самый дорогой дефект в проекте: он одновременно ломает визуал, прогресс и главный способ
активации.

### 2.2 🔴 Тяжёлая работа внутри low-level mouse hook

`MousePushToTalkService.HookCallback` синхронно выполняет:

| Что | Где | Порядок стоимости |
|---|---|---|
| `Process.GetProcessById` + `process.MainModule?.FileName` | `GameForegroundPolicy.cs:54-58` | десятки мс на холодную |
| `Directory.EnumerateFiles` + `GetCreationTimeUtc` + `Delete` | `AudioCaptureService.cs:192-208` | дисковый I/O, при AV-скане — сотни мс |
| `new WaveInEvent(...)` + `StartRecording()` (`waveInOpen`) | `AudioCaptureService.cs:39-52` | 20–150 мс |
| Полный цикл WPF-layout + Storyboard + `ShowCapsule()` | `MainWindow.Visuals.cs:80-91` | 3–30 мс |

Всё это внутри колбэка, ограниченного 300 мс. `Process.MainModule` перечисляет модули **чужого**
процесса — это блокирующий межпроцессный вызов.

### 2.3 🔴 `Dispose` рецогнайзера не защищён `_decodeLock`

`GigaAmTranscriptionService.Dispose:157-166` вызывает `_recognizer?.Dispose()` (:159) без взятия
`_decodeLock`, а `HybridTranscriptionService.Dispose:145` вызывает его безусловно. Если
`MainWindow.Dispose` (`MainWindow.xaml.cs:800`) отработает во время активного декода —
освобождение нативной ONNX-сессии под работающим `Decode()` → падение процесса. Симметрично
`_decodeLock.Dispose()` (:160) при ждущих на семафоре.

---

## 3. Скорость: где именно теряются миллисекунды

Распознавание **не начинается до отпускания кнопки**. Конвейер целиком файловый: для
60-секундной диктовки пользователь ждёт полное время декода 60 секунд аудио **после** отпускания.

Замеры из репозитория (`artifacts/audit/Egoist-Voice-1.6.2-audit.md:17-18`): гибрид p95 —
**406,3 мс**, установленный билд p95 — **361,1 мс**. `docs/research.md:21,23`: GigaAM decode
0,095 с, Whisper warm ~0,28 с на одной и той же 11,29-секундной записи.

**Отсюда главный вывод по скорости: Whisper практически целиком определяет p95 — ~0,28 с из
~0,4 с — при том что для чисто русской речи его результат почти всегда отбрасывается.**

Бюджет тёплого прохода:

| Стадия | Где | Оценка |
|---|---|---|
| hook → UI «Распознаю» | `MainWindow.xaml.cs:488-490` | 3–30 мс |
| остановка + дренаж MME (3 буфера × 32 мс) | `AudioCaptureService.cs:67-89` | 10–100 мс |
| flush WAV + `FileInfo` + синхронный `AppLog` | `AudioCaptureService.cs:145`, `MainWindow.xaml.cs:498-502` | 2–15 мс |
| `AreAllModelsReady`: 5 × (stat + read + JSON-парсинг) | `HybridTranscriptionService.cs:50`, `ModelManager.cs:407-424` | 1–10 мс, с AV — больше |
| повторное чтение и конверсия WAV для GigaAM | `GigaAmTranscriptionService.cs:78` | 5–40 мс |
| **max(GigaAM decode, Whisper decode)** | `HybridTranscriptionService.cs:62` | **доминирует** |
| clipboard (до 6 попыток, 35→175 мс) | `ClipboardService.cs:16-26` | 5–50 мс, до 525 мс при конфликте |
| `Task.Delay(80)` фиксированный | `TextInsertionService.cs:28` | 0 или 80+ мс |
| SendInput Ctrl+V | `TextInsertionService.cs:36-44` | <1 мс + обработка в приложении |

Дополнительно:

- **Файл читается трижды**: `FileInfo` (`MainWindow.xaml.cs:498`), `AudioSampleReader`
  (`GigaAmTranscriptionService.cs:78`), `File.OpenRead` (`WhisperTranscriptionService.cs:66`).
- `WhisperProcessor` создаётся заново на каждую диктовку
  (`WhisperTranscriptionService.cs:67,79-83`) — нативный контекст + токенизация промпта.
- **Параллелизм между чанками GigaAM отсутствует**: строгий `for` под `_decodeLock`
  (`GigaAmTranscriptionService.cs:85-99`). Двухминутная запись = 6 чанков строго последовательно.
- **Прогрева GigaAM нет.** `WarmUpAsync` (:28-69) только создаёт `OfflineRecognizer`. Первый
  реальный декод оплачивает graph optimization и инициализацию арены ONNX Runtime.
- `GigaAmTranscriptionService.cs:136` — `Provider = "cpu"`. На машине с RTX 5070 Ti GigaAM
  считает на CPU.
- Тишина в начале и конце записи подаётся в оба движка без обрезки.

---

## 4. Точность: чего нет

| Пробел | Подтверждение |
|---|---|
| **Пользовательского словаря нет** | `TechnicalTermCatalog.cs:5-12` — 44 зашитых термина, `internal`, без UI и файла |
| **Словарь не влияет на GigaAM вообще** | Он идёт в `initial_prompt` Whisper (:14-17) и в эвристику селектора. sherpa-onnx поддерживает hotwords, но только при `modified_beam_search`, а в коде `DecodingMethod = "greedy_search"` (`GigaAmTranscriptionService.cs:144`) |
| **Контекста нет** | Новый `CreateStream()` на каждый чанк (:91) — RNNT не видит левый контекст. Между диктовками контекста нет. `_targetWindow` используется только для вставки, не для профиля распознавания |
| **Чисел нет** | Ни «двадцать пять» → «25», ни дат, ни единиц, ни процентов. Grep не даёт ни одного числового нормализатора |
| **Голосовых команд нет** | «новая строка», «запятая», «удали последнее слово» не обрабатываются |
| **Форматирование минимально** | `TranscriptNormalizer` — ровно 4 правила: схлопывание пробелов, пробел перед пунктуацией, скобки, заглавная первая буква |
| **Два разных алгоритма абзацевания** | `TranscriptFormatter` применяется только к Whisper (`WhisperTranscriptionService.cs:76`); для GigaAM работает `TranscriptChunkJoiner.Join` |
| **Нет per-app профилей** | Признано известным пробелом в `artifacts/audit/Egoist-Voice-1.6.2-audit.md:28-31` |
| **Нет корпуса и метрик** | `docs/research.md:39` прямо констатирует: нужен корпус 50–100 фраз для WER/CER. Пороги селектора настроены на единичных демо-записях |

### 4.1 VAD, которого нет

Настоящего VAD в проекте нет (ни Silero, ни WebRTC, ни sherpa-onnx VAD). Есть два независимых
энергетических механизма.

**`SpeechActivityDetector`** — гейт «была ли речь вообще»: RMS ≥ −48 dBFS **и** peak ≥ −38 dBFS,
суммарно ≥160 мс речи и непрерывный отрезок ≥96 мс. Используется **только** для решения
«пропустить сессию целиком». Тишину он не режет. Пороги абсолютные, без адаптации к шумовому
полу: в шумной комнате гейт всегда открыт, при тихом микрофоне сессия молча выбрасывается без
сообщения пользователю.

**`GigaAmAudioChunker`** — нарезка >22 с. Здесь три реальные ошибки:

1. **Overlap применяется только в fallback-ветке.** Строки :242-244 — при найденной тишине
   `start = end`, перекрытие **ноль**; и только если тишины нет — `end − 240 мс`.
2. **`SelectBoundary` (:274-287) при равных кандидатах выбирает самый правый**
   (`candidate.Boundary > current.Boundary`), а не самый длинный прогон тишины. То есть
   предпочитается граница ближе к 22 с, а не самая надёжная пауза.
3. **Дедупликация стыков фактически не работает.** `TranscriptChunkJoiner.FindExactOverlap`
   (:310-326) сравнивает токены как есть, `OrdinalIgnoreCase`, без нормализации пунктуации — а
   GigaAM v3 e2e выдаёт пунктуацию. Поэтому `"три,"` ≠ `"три"`, и на стыках остаются дубли слов.

Порог тишины `SilenceRms = 0.009` (~−40,9 dBFS) абсолютный. При отсутствии тишины (шумная среда,
непрерывная речь) режется жёстко на 22 с посреди слова.

### 4.2 Гибридный селектор

`MixedLanguageTranscriptSelector.Select` (`HybridTranscriptionService.cs:172-196`) — чистая
детерминированная функция от двух строк. Пороги:

```csharp
comparableLength     = whisper.Length >= giga.Length*0.55 && <= giga.Length*1.8       // :185
preservesMoreEnglish = whisperLatin >= max(2, gigaLatin+2) || whisperTech > gigaTech  // :187-188
predominantlyEnglish = whisperLatinRatio >= 0.45 && >= gigaLatinRatio + 0.15          // :189-190
credibleEnglish      = predominantlyEnglish && whisperLatin >= 3                      // :192
Whisper выбирается если: (comparableLength && preservesMoreEnglish) || credibleEnglish // :193
```

Проблемы:

1. **`credibleEnglish` обходит проверку длины.** Галлюцинация Whisper в 5 раз длиннее вывода
   GigaAM, но с латиницей ≥45 %, выигрывает безусловно.
2. **Выбор «всё или ничего».** Нельзя взять русский от GigaAM и английские термины от Whisper.
   Выбрав Whisper, теряем «ё» и лучшую русскую пунктуацию; выбрав GigaAM — теряем термины.
3. Whisper зафиксирован на `WithLanguage("ru")` (`WhisperTranscriptionService.cs:80`) — ветка
   «преимущественно английская речь» концептуально противоречит принудительно русскому декоду.

---

## 5. UI: визуал, анимации, производительность

### 5.1 Геометрия и слои

| Слой | Элемент | Геометрия | Цвет |
|---|---|---|---|
| Окно | `Window` | 242 × 72, `AllowsTransparency`, `Background=Transparent` | — |
| 0 | `ShadowSurface` → 3 × `PixelPerfectCapsuleBorder` | `Margin` 6/8/10, радиусы 30/28/26, `PhysicalStroke=1` | `#0C000000` / `#16000000` / `#26000000` |
| 1 | `RootBorder` | `Margin=12` → тело **218 × 48**, радиус 24, `PhysicalStroke=1.6`, `RasterScale=4` | fill `#08080A`, stroke `#2A2A30` / `#D91F2B` |
| 2 | `SurfaceGradient` | радиус 23, вертикальный градиент | `#0D0D10` → `#050506` @0.68 → `#020203` |
| 3–4 | `HoverSurface` / `SuccessFlash` | радиус 23 | `#10FF2634` / `#20FF2634` |
| 5 | Контент-сетка | колонки `36 \| 1 \| * \| 1 \| 32` | — |

Ширины по состояниям: Ready/Listening 242, Recognizing/Error 256, Success/Clipboard 248,
Downloading 304.

### 5.2 Почему `PixelPerfectCapsuleBorder` дорогой

Это `Decorator`, который рендерит хром во внеэкранный `RenderTargetBitmap` с суперсэмплингом ×4
и кладёт результат как картинку (:64-89). Причина в комментарии :59-63 — однопиксельная дуга в
WPF растеризуется одним сэмплом на выходной пиксель, и на радиусе 24 соседние пиксели кривой
чередуются между яркими и почти прозрачными. Обход технически правильный. Цена — нет.

При DPI 1,5 и `RasterScale=4` только для `RootBorder` это `218 × 1,5 × 4 = 1308 × 288` Pbgra32
≈ **1,5 МБ на кадр**, плюс три тени по ~0,28 МБ. Итого **~2,3 МБ аллокаций и 4 GPU-прохода на
каждый ре-рендер**. `OnRender` срабатывает при каждом изменении `ActualWidth` — то есть при
анимации ширины на 144 Гц это ~26 кадров × 4 RTB ≈ **40+ МБ за 180 мс**, часть в LOH.

Дополнительно `RenderOptions.SetBitmapScalingMode(this, ...)` вызывается **внутри** `OnRender`
(:88) — установка DP во время отрисовки.

### 5.3 Анимация ширины окна — самый тяжёлый путь

`AnimateCapsuleWidth` (`MainWindow.Visuals.cs:295-326`):

```csharp
_widthAnchorCenter = Left + currentWidth / 2;
Width = targetWidth;                                    // :303 ← кадр финальной ширины перед откатом на From
var animation = new DoubleAnimation { From = currentWidth, To = targetWidth,
    Duration = TimeSpan.FromMilliseconds(180),
    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
    FillBehavior = FillBehavior.Stop };
BeginAnimation(WidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
```

Анимируется **`Window.Width`**, то есть ресайз HWND каждый кадр. Плюс `SizeChanged`
(`MainWindow.xaml.cs:116-122`) отдельно двигает `Left` — **два `SetWindowPos` на кадр, не
атомарно**. На 144 Гц это 144 ресайза окна в секунду, каждый с полным layout и каскадом RTB.

### 5.4 Waveform

24 `Border` в `ItemsControl` со `StackPanel` — обычные WPF-элементы, не `DrawingVisual`.
Геометрия: 24 полосы 2 × 22 DIP, шаг 5, `CornerRadius=1`. Цвет — 24 замороженные кисти,
`Color.FromRgb(224 + 31w, 29 + 18w, 45 + 14w)`: центр ≈ `#FE2E3A`, края ≈ `#E11E2E`. Разница
едва различима — визуально это плоская красная «расчёска».

Сглаживание сделано грамотно: асимметричный attack/release в `AudioCaptureService`
(`smoothing = level > _smoothedLevel ? 0.62f : 0.20f`) плюс второй экспоненциальный фильтр
τ=35 мс на каждую полосу.

Но частота обновления зафиксирована жёстким порогом:

```csharp
if (_lastWaveFrame != TimeSpan.Zero && elapsed < TimeSpan.FromMilliseconds(12)) return;  // :336-352
```

60 Гц → 60 fps; **120 Гц → 60 fps** (каждый второй кадр); **144 Гц → 72 fps**; 240 Гц → 80 fps.
Storyboard'ы при этом идут на полной частоте композитора. **Разная каденция движения в одном
кадре** — волна визуально «ступенчатая» на фоне плавного пульса.

На кадр: 24 бокса `double` от `ScaleY` + 24 от `Opacity` ≈ 3 450 боксов/с при 72 fps. `barAlpha`
считается внутри цикла, хотя зависит только от `deltaSeconds`. `Opacity` присваивается
одинаковое значение всем 24 полосам — 23 из 24 записей избыточны.

### 5.5 Анимации: полный перечень

| Ключ | Свойство | Длительность | Easing |
|---|---|---|---|
| `SpinStoryboard` | `SpinnerOrbitRotate.Angle` 0→360 | **820 мс** | **линейно**, Forever |
| `ListenPulseStoryboard` | `StateHalo.Opacity` 0.05→0.24 + scale 0.94→1.07 | 720 мс | `SineEase InOut`, AutoReverse |
| `ProcessingStoryboard` | — | — | **пустой мёртвый ресурс** |
| `DownloadStoryboard` | `DownloadArrowOffset.Y` −0.8→0.8 | 620 мс | `SineEase InOut` |
| `SuccessStoryboard` | `CheckScale` 0.64→1 | 180 мс | `BackEase EaseOut, Amplitude=0.16` |
| `ErrorStoryboard` | `StateShake.X` ±2.5 px | 200 мс | keyframes без функции |
| `EnterStoryboard` | scale 0.972/0.95→1, translate Y 2.5→0 | **190 мс** | `CubicEase EaseOut` |
| `ExitStoryboard` | scale →0.984/0.97, translate Y →1.5 | **150 мс** | **easing отсутствует** |
| `StateTransitionStoryboard` | `CenterContent.Opacity` 0.32→1 | 140/160 мс | `CubicEase EaseOut` |
| Hover | `HoverSurface.Opacity` | 167 мс | линейно |

`SetProcessingState` вызывает `StopStateAnimations()` + `BeginStateStoryboard("SpinStoryboard")`
(`MainWindow.Visuals.cs:100-101`) → **при каждом обновлении прогресса спиннер прыгает на 0°**.
Он же вызывает `ShowCapsule()` → `KeepCapsuleOnScreen()` → `SetWindowPos` на каждое обновление
прогресса.

`SuccessStoryboard` анимирует `CheckScale`, но в состоянии `Clipboard` виден `ClipboardIcon` —
**pop-анимация играет на скрытом элементе**.

`CanCancel=true` формально только у `Listening`, но `SetProcessingState` создаёт состояние с
`CanCancel` из выражения `percentage is null` — **отмена во время распознавания появляется как
побочный эффект отсутствия процента**. Неявно и хрупко.

### 5.6 Windows-интеграция: где промахи

**DPI — самая проблемная зона.**

- `app.manifest` содержит **только** `longPathAware`. Секции `<dpiAware>` / `<dpiAwareness>`
  **нет**.
- `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` в `.csproj:12` — свойство
  генератора WinForms; работает только через `ApplicationConfiguration.Initialize()`, которого в
  проекте **нет**. В WPF-приложении со своим `App.OnStartup` оно не даёт ничего.
- `OnDpiChanged` нигде не переопределён, `WM_DPICHANGED` не обрабатывается.
- В `ClampToVirtualScreen` (`MainWindow.xaml.cs:684-691`) `Width`/`Height` (DIP окна)
  сравниваются с `VirtualScreen*` (DIP основного монитора) → на конфигурации 100 % + 150 %
  ограничение промахивается на коэффициент масштаба.
- Ручной DPI-хак в трее (`EgoistTrayRenderer.cs:73,94`: `Math.Max(1f, e.Graphics.DpiX / 96f)`) —
  прямое следствие неинициализированного WinForms-DPI.

**Позиционирование.** `Clamp` ограничивает по `VirtualScreen`, а не по work area монитора.
Следствие: капсула может лечь под панель задач или в «дыру» между мониторами разной высоты и
оказаться невидимой. Подписки на `SystemEvents.DisplaySettingsChanged` нет — при отключении
монитора капсула остаётся за экраном до следующего показа.

**Topmost** ставится один раз в XAML и никогда не переподтверждается. `SW_SHOWNOACTIVATE` не
меняет Z-порядок внутри группы topmost → оверлей Discord/Steam легко перекроет капсулу.

**`TextRenderingMode="ClearType"` на окне с `AllowsTransparency="True"` — no-op**: WPF на
layered-окнах принудительно откатывается на grayscale-антиалиасинг. Атрибут вводит в
заблуждение.

**`uiAccess="false"`** → над окнами UAC и elevated-приложений капсула не появится, хоткеи там не
сработают, `SendInput` не пройдёт.

**Ни одного DWM-API**: ни `DWMWA_SYSTEMBACKDROP_TYPE`, ни `DWMWA_WINDOW_CORNER_PREFERENCE`, ни
`DWMWA_USE_IMMERSIVE_DARK_MODE`. `SetWindowDisplayAffinity` тоже не используется — капсула
попадает в скриншоты и записи экрана.

**Трей-иконка.** `Icon.ExtractAssociatedIcon(Environment.ProcessPath)` (`TrayService.cs:248`)
возвращает **один** кадр (обычно 32 × 32), хотя `EgoistVoice.ico` содержит 7 кадров (16, 24, 32,
48, 64, 128, 256). При 125 %/150 % трей запрашивает 20/24 px и получает масштабированную вниз
32-ку. Иконка статична — не меняется при записи, распознавании или ошибке.

**Меню трея** — `RoundedEdges = false` + прямоугольная рамка: квадратные углы, без тени, без
акрила, без анимации открытия, без иконок пунктов.

### 5.7 Хоткеи

`GlobalHotkeyService`: нажатие через `WM_HOTKEY` — мгновенно. **Отпускание — опросом**,
`DispatcherTimer` 16 мс с `DispatcherPriority.Input` (:39-43). Латентность 0–16 мс плюс очередь
диспетчера, а `Input` ниже `Render`, поэтому во время анимации тик может уехать.

`GameForegroundPolicy` — хардкод 19 имён процессов + 6 маркеров путей. При совпадении Mouse 5
**полностью игнорируется**, то есть диктовка в играх не работает вообще. Для игры не из списка
работает, но с риском фриза из §2.2. Список неизбежно устареет; нет ни настройки, ни исключений,
ни UI-индикации «в игре отключено».

---

## 6. Вставка текста

Механизм: **clipboard + SendInput(Ctrl+V)**. `WM_CHAR` и `KEYEVENTF_UNICODE` не используются.

| Проблема | Место |
|---|---|
| **UIPI**: `asInvoker`, `uiAccess="false"` → в elevated-окна `SendInput` не пройдёт, и **не сообщит об ошибке** | `app.manifest:7` |
| **`ScanCode = '\0'` без `KEYEVENTF_SCANCODE`** → приложения, читающие скан-коды (DirectInput-игры, RDP, часть VM), нажатия не увидят | `TextInsertionService.cs:55-68` |
| **Ctrl+V не универсален**: терминалы (Ctrl+Shift+V), vim/Emacs, поля с перехватом | :47-53 |
| **Залипшие модификаторы**: состояние клавиатуры перед `SendInput` не санируется | :36-44 |
| **`ExtraInfo = UIntPtr.Zero`** — свой инжект не помечен, приложения с хуками не могут его отфильтровать | :66 |
| **Буфер обмена затирается навсегда** — сохранения/восстановления прежнего содержимого нет нигде | `ClipboardService.cs` |
| **Верификации вставки нет** в продакшн-пути (есть только в смоук-тесте) | `App.xaml.cs:533-536` |
| `ClipboardService` ловит только `COMException` — `ExternalException`/`ThreadStateException` пролетят мимо ретраев | :20 |

Важная скрытая хрупкость: `ClipboardService` зависит от того, что вызывающий поток — STA. Сейчас
это выполняется **случайно** (продолжение возвращается в Dispatcher). Любой `ConfigureAwait(false)`
выше по стеку сломает вставку с `ThreadStateException`. Это прямо связывает §2.1 с §6: исправлять
их нужно одним изменением.

---

## 7. Прочие находки

| № | Находка | Место |
|---|---|---|
| 7.1 | **Утечка временного WAV при отмене.** `audioPath` присваивается только после `await StopAsync`; при отмене остаётся `null`, `finally` ничего не удаляет — файл живёт сутки до `DeleteStaleRecordings` | `MainWindow.xaml.cs:492-497,556-563` |
| 7.2 | `CloseButton_OnClick` проверяет `_isRecording`, который к моменту клика во время распознавания уже сброшен → `CancelAsync` не вызывается | :566-578 |
| 7.3 | **Гонка `_whisperWarmUp ??=`** без синхронизации: вызывается и из `BeginWarmUp` при старте, и из `TranscribeAsync`. Возможен запуск двух прогревов и перезапись поля | `HybridTranscriptionService.cs:41` |
| 7.4 | **Блокирующий `Wait(2 s)` на UI-потоке при выходе** — закрытие приложения может подвиснуть на 2 секунды | :138 |
| 7.5 | `_operationCancellation?.Dispose()` в начале `StartRecording`, при том что предыдущая операция могла ещё владеть токеном | `MainWindow.xaml.cs:465` |
| 7.6 | **Дисковая запись внутри аудио-колбэка** под общим локом: задержка диска → переполнение буферов MME → **потеря аудио** | `AudioCaptureService.cs:110-112` |
| 7.7 | Ошибка «Запись уже запущена» показывается как «Нет микрофона». Корректный `GetMicrophoneError` — **мёртвый код, не вызывается ниоткуда** | :24, `MainWindow.xaml.cs:478-482,747-754` |
| 7.8 | `ModelManager.Report` на **каждом** отчёте (раз в 200 мс во время загрузки) парсит JSON пяти marker-файлов | `ModelManager.cs:364-405` |
| 7.9 | Мёртвые параметры: `force` в `Report` (`_ = force`), `activePath` в `CleanupSupersededModels` | :374, :465 |
| 7.10 | `FindExactOverlap` сравнивает весь накопленный текст с началом нового чанка — при повторе фразы возможно ложное удаление до 12 слов | `GigaAmTranscriptionService.cs:310` |
| 7.11 | **Доступность на нуле**: ни одного `AutomationProperties.*` во всём проекте. Окно `Focusable=False` + `WS_EX_NOACTIVATE` → клавиатурная навигация невозможна, Esc не отменяет, скринридер не объявляет ни смену состояния, ни прогресс | весь XAML |
| 7.12 | `Cursor="SizeAll"` + `ToolTip="Перетащить"` на **всей** площади капсулы — подсказка всплывает поверх капсулы во время диктовки | `MainWindow.xaml:134-136` |
| 7.13 | Handle delta 6–11 за прогон (не ноль) — стоит проверить `CreateStream()` и `WhisperProcessor` на полное освобождение | `artifacts/audit/...1.6.2-audit.md:17-18` |

---

## 8. Резюме: три вывода

1. **Скелет здоров, три слоя требуют перестройки.** Чистая логика вынесена и покрыта тестами,
   настройки атомарны, смена биндинга транзакционна. Переписывать с нуля незачем.

2. **Один дефект стоит дороже остальных вместе взятых.** Декод на UI-потоке (§2.1) одновременно
   ломает анимацию, прогресс и — через `LowLevelHooksTimeout` — сам механизм активации. Он же
   блокирует любые улучшения визуала: полировать анимацию, которая замирает на время
   распознавания, бессмысленно.

3. **Половина p95 тратится на движок, результат которого почти всегда выбрасывается.** Whisper
   даёт ~0,28 с из ~0,4 с и запускается безусловно на каждой диктовке, включая чисто русскую
   речь. Это самая дешёвая крупная оптимизация в проекте.
