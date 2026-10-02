# Egoist Voice 2.4.0 — публичная проверка качества

Проверено на 80 заранее выбранных человеческих записях: 40 литературных чтений и 40 студийных разговорных реплик, 1169 reference words, 578.1467 секунды. Каждая модель получала одно и то же сохранённое PCM16 mono 16 kHz; reference не менялся после просмотра результатов. Это не персональный корпус пользователя и не независимый набор сложных фонем/шумной речи/английских брендов.

## Итоговая связка внутри приложения

Plain GigaAM v3 RNNT INT8 определяет слова. E2E RNNT INT8 распознаёт то же аудио для пунктуации/регистра; bounded alignment проецирует оформление на основной текст. Известные названия заменяются только при exact Latin canonical во вторичном аудиораспознавании и согласии всего фрагмента. Неизвестные названия, обычные слова, URLs, пути, версии и структурированный текст защищены.

| Метрика | Plain 2.3 baseline | Quality 2.4 | Изменение |
|---|---:|---:|---:|
| Ошибки слов, ignoring case/punctuation, ё→е | 26/1169 | 26/1169 | без изменения |
| Пунктуация, FP + FN | 277 | 130 | −53.07% |
| Raw character errors, including case/punctuation | 573/7228 | 241/7228 | −57.94% |
| Surface word errors | 493 | 217 | −55.98% |
| Пунктуация F1 | 0 | 0.76786 | новое оформление |
| Медиана warm decode | 75.98 мс | 162.44 мс | +86.46 мс |
| p95 warm decode | 183.16 мс | 323.78 мс | +140.62 мс |
| CPU time за 80 записей | 47.02 с | 100.84 с | ~2.14× во время обработки |

Цель около 50% достигнута в этих метриках оформления. Рост точности распознанных слов на 50% не подтверждён. Прирост скорости не заявляется. Измерения времени — один парный проход на Ryzen7 9800X3D; фоновые нагрузки пользователя не отключались. GPU разрешён, но выбранный CPU pipeline оказался точнее проверенных крупных кандидатов на этих данных.

Quality-only ready private memory до загрузки дополнительного независимого baseline engine: 746856448 байт; восемь model files: 650090519 байт. Пик полного сравнительного harness включает третий распознаватель и не является пиком production приложения.

## Парное сравнение моделей

| Вариант | Book40 ошибок/572 | Dialogs40 ошибок/597 | Всего/1169 |
|---|---:|---:|---:|
| Stock plain RNNT INT8 | 16 | 10 | 26 |
| Stock E2E RNNT INT8 | 25 | 23 | 48 |
| Финальная связка 2.4 | 16 | 10 | 26 |
| Stock plain RNNT FLOAT export | 17 | 8 | 25 |
| Official original GigaAM plain, GPU | 24 | 12 | 36 |
| Official original GigaAM E2E, GPU | 24 | 23 | 47 |
| AW Russian Whisper Turbo, CT2 GPU int8_float16 | 43 | 10 | 53 |
| Podlodka Whisper large-v3 Q8, native Vulkan beam | 65 | 13 | 78 |
| NVIDIA Parakeet TDT0.6Bv3 INT8 | 80 | 19 | 99 |
| Qwen3-ASR1.7B native Q8, correct empty SYSTEM | 110 | 15 | 125 |
| Qwen3-ASR1.7B original BF16 GPU | 108 | 16 | 124 |

FLOAT encoder экономит одну lexical error на всём наборе, но native RAM примерно 2.5× больше, CPU decode медленнее. Исправленный экспериментальный native frontend соответствует official feature extraction численно, но ухудшает lexical errors 26→34 на этом наборе, поэтому не поставляется. RUPunct tiny text formatting помогает Dialogs, ухудшает Book и не получает аудио. Больший размер модели сам по себе не доказал улучшения русской речи.

Official original default encoder autocastFP16 и explicitFP32 дали одинаковые строки всех 80 записей в обоих вариантах. Это не тест resident-half default GitHub loader. Первоначальный неверный Qwen chat template исключён из итогового сравнения.

## Проверки надёжности и границы

Actual service 2.4: 80/80 результатов с Completed formatting, три silence fixtures без слов, file/memory parity, неизменность исходного PCM, formatting-off exact primary result, отмена и восстановление после неё. 111.15 секунды аудио обработаны фрагментами за 2482 мс в этом проходе. Первый парный проход использовал NuGet CAPI SHA256 614878147c05121aeb1514ec4fb3e48b89751591532eca9208235b9ab868306a. Финальная зависимость — официальная no-TTS x64 shared MT 1.13.4, CAPI dcfc89cf50fbd0fb77c115a6b53ee9b2739e57d6d1f4158a3aeab31dd7139676: все 80 полных результатов связки совпали точно, отдельно прошли silence/file/cancel/recovery/long-audio проверки. Исходный frontend сохранён; экспериментальное исправление не применяется. ONNX Runtime daa77083a45bf525da0dde9e87f85d8eb146f58f9c9aa7124ca84545e1c0f148 неизменён. Native модели реально загружены из пути с кириллицей; полные native notices и источник MPL-компонента поставляются в licenses/native-asr.

Full: 1037 passed. Compact: 1030 passed, 7 Full-only download tests skipped. Formatter initialization/decode failure, backoff, cancellation, dispose/warmup races, protected syntax, conservative names и legacy settings проверены адресными тестами. Публичные записи не содержат Latin brand gold; 53 name tests проверяют безопасные правила, не реальную acoustic name accuracy. Пунктуационная разметка не равна gold-интонации. Новая связка не является свободным исправлением орфографии/грамматики и не домысливает произнесённые слова.

## Источники и воспроизводимость

- [GigaAM](https://github.com/salute-developers/GigaAM), [pinned export](https://huggingface.co/Smirnov75/GigaAM-v3-sherpa-onnx/tree/6888903da215c7735f51101d939f3bfa679fb2b8), MIT.
- [russian_librispeech](https://huggingface.co/datasets/istupakov/russian_librispeech), revision a519c986bb3342cc8136d3d14e5ad8a4f1e1a2bd.
- [Dialogs](https://huggingface.co/datasets/langswap/dialogs-ru-emotional-conversations), revision e25ba617b2b56bd1dbf255d3905c51bd8da3d31f; studio actors, emotional labels, not phoneme/prosody gold.
- [Parakeet](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3), [sherpa runtime](https://github.com/k2-fsa/sherpa-onnx/tree/142807252687d81b40d6315f23470a1512a00de3), [Qwen3-ASR](https://github.com/QwenLM/Qwen3-ASR).

Локальные source-linked receipts: artifacts/quality-2.4.0/quality-native-public80, parakeet-public80 и tests; остальные кандидаты — artifacts/quality-2.3.1. Аудио/предсказания не входят в Git или release assets. Release содержит обезличенные aggregate metrics и pinned model manifest. Исторический Full и чистая Windows VM installer lifecycle остаются отдельно; installer на рабочем компьютере не запускается для проверки.

## Точный опубликованный payload и рабочая установка

Релиз [61611dc / v2.4.0](https://github.com/egoist-ai1/EgoistVoice/releases/tag/v2.4.0) использует официальный no-TTS CAPI dcfc89… и unchanged ORT. Отдельный final shipped80 проход дал точно те же 80 outputs и ошибки 26/130/241: warm median135.005 мс, p95267.8291 мс, CPU87671.875 мс, startup2008.8457 мс, ready private738918400 байт и peak working set1101168640 байт. Это двухдвижковый harness без WPF; длинные 111.15с обработаны за 2203.44 мс. Времена этого прохода не заменяют парную таблицу выше и не доказывают изменение скорости относительно первого native. [Aggregate receipt](../../artifacts/quality-2.4.0/shipped-payload-native80/aggregate-only.json).

Публикация и транзакционное обновление завершены. Все 545 установленных файлов совпали с final manifest; версия 2.4.0.0, ASR/formatter ready, punctuation enabled. Сохранены 14 прежних свойств настроек, preference истории и 5 Data-файлов. Actual installed EXE public-audio smoke exit0, exact accepted output; изолированные Data/logs, без download и личного аудио. Живой WPF после старта: private833245184 байт, working set902422528 байт, startup peak1235329024 байт. Это startup снимок, не нагрузка диктовки. [Readback](../../artifacts/quality-2.4.0/release-qa/workstation-readback.json), [installed smoke](../../artifacts/quality-2.4.0/release-qa/installed-cli-smoke.json).

Full/clean Windows CI 1037; Compact 1030+7Full-onlyskips; Pester 34, Analyzer 0, UI 21previews/10layouts; независимые final package/native/API/notices/hash guards passed. Live cross-process DLL listing недоступен из-за Windows ACCESS_DENIED5: точная EXE и installed hashes проверены, actual native load hashes доказаны отдельным exact-payload harness. Installer unsigned, на рабочей машине не запускался; clean-Windows installer lifecycle не подтверждён. Старые 2.3tag/assets сохранены. Обновление этой документации после выпуска не меняет tag и release assets.
