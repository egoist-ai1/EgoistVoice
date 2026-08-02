# Egoist Voice: handoff локального кандидата после 2.1.0

Дата фиксации состояния: 27 июля 2026 года.

## Статус

Последняя опубликованная версия — **2.1.0**. Текущий `HEAD` совпадает с
`origin/main`: `f1f5997` (`Версия установщика берётся из csproj, а не из
литерала в скрипте`). Поверх него находится незакоммиченный локальный кандидат:
10 изменённых и 3 новых файла.

Это **не готовый релиз**. Проект по-прежнему объявляет версию `2.1.0`; новый
установщик не собирался, installer payload и lifecycle не проверялись, commit,
tag, push и публикация не выполнялись. По содержанию изменения подходят для
будущей версии **2.1.1**, но этот номер ещё не присвоен.

`dotnet test` компилировал приложение в `bin/Release` как зависимость тестов.
Это не заменяет self-contained publish, упаковку Inno Setup и проверку
установщика.

## Отличия от опубликованной 2.1.0

| Область | Опубликованная 2.1.0 | Локальный кандидат |
|---|---|---|
| Команда перевода | Узнаёт также неоднозначные формы «перевод», «перевёл», «перевести» | Только явные повелительные формы: «переведи», «переведите», «переводи», «переводите» |
| Обычная диктовка | Фраза «Перевод денег задержался» могла запустить перевод | Существительное, прошедшее/будущее время и инфинитив остаются обычным текстом |
| Общий порт 47821 | Достаточно успешного `/health` | До отправки текста `/v1/models` должен подтвердить HY-MT |
| HTTP-ошибки | Ответ мог разбираться без проверки статуса | Non-2xx и повреждённый JSON безопасно возвращают исходную диктовку |
| Выбор модели | Подходил произвольный самый большой `.gguf` | Локальный файл обязан содержать `HY-MT` в имени |
| Завершение приложения | `TranslatorClient` не освобождался из `MainWindow.Dispose` | HTTP-клиенты, sidecar и Job Object освобождаются детерминированно; `Dispose` идемпотентен |
| Release smoke | В двух скриптах сохранялся литерал `2.0.0` | Версия читается из `Egoist.Voice.csproj` |
| Тестовые логи | Мог использоваться пользовательский `%LOCALAPPDATA%\EgoistVoice\Logs` | `EGOISTVOICE_LOG_DIRECTORY` направляет тесты во временную директорию |

## Изменённые файлы

- `Core/TranslateCommandParser.cs` — сужение грамматики команды.
- `Services/TranslatorClient.cs` — HY-MT identity check, HTTP error handling,
  фильтр модели и детерминированное освобождение ресурсов.
- `MainWindow.xaml.cs` — вызов `_translator.Dispose()`.
- `Services/AppLog.cs` — изоляция каталога логов через environment variable.
- `scripts/test-installer.ps1` и `scripts/full-release-smoke.ps1` — версия из
  `.csproj`.
- `tests/Egoist.Voice.Tests/TranslateCommandParserTests.cs` — regression cases
  для обычных грамматических форм.
- Новые `TranslatorClientTests.cs`, `ReleaseContractTests.cs` и
  `TestEnvironment.cs` — HTTP-, release- и log-isolation contracts.
- `README.md`, `CHANGELOG.md`, `docs/COLLABORATION.md` — актуализация статуса и
  числа тестов.

## Что доказывают проверки

Свежий прогон текущего working tree:

- `dotnet test .\tests\Egoist.Voice.Tests\Egoist.Voice.Tests.csproj -c Release --no-restore` — **387/387**, 0 failed, 0 skipped.
- Regression cases подтверждают, что «Перевод денег задержался», «Я закончил
  перевод», «Перевёл документ вчера», «Я закончил документ, а ты перевёл»,
  «Переведёшь документ завтра» и «Перевести документ было сложно» не являются
  командами.
- `TranslatorClientTests` используют in-memory HTTP handlers: приватный текст
  не уходит в сеть; проверяются HY-MT identity, non-2xx, malformed JSON и
  повторный `Dispose`.
- Локальный GigaAM stress на одном закреплённом sample: 20/20 стабильных
  итераций, cold 290,2 мс, p50 232,9 мс, p95 261,9 мс, max 269,4 мс,
  `handleDelta=-3`, одинаковый `textSha256`. Исходный отчёт:
  `artifacts/stress-source-final.txt`.

387 unit/integration tests подтверждают source contracts, но **не** доказывают
качество нового установщика или поведение на чистой машине.

## Что не проверено

- self-contained publish и новый Inno Setup installer;
- состав installer payload и наличие всех моделей/native runtimes;
- install → launch → microphone dictation → HY-MT translation → upgrade →
  uninstall на чистой Windows;
- несовместимый реальный сервис на порту 47821 и время cold-start HY-MT;
- WER/CER на пользовательском голосовом корпусе;
- Authenticode, финальный SHA-256, tag и GitHub release.

## Как завершить будущую 2.1.1

1. Просмотреть working-tree diff и присвоить `2.1.1` в
   `Egoist.Voice.csproj` и release notes.
2. Повторить полный Release build и 387 tests.
3. Запустить `scripts/build-installer.ps1` и проверить identity, payload,
   version metadata и SHA-256.
4. В чистой Windows VM выполнить install → launch → реальная диктовка → HY-MT
   translation → upgrade с 2.1.0 → uninstall.
5. Только после этого решить отдельно, нужны ли commit, tag, push и публикация.

