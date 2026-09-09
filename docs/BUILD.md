# Сборка и состав поставки

[← На главную](../README.md)

Нужны Windows x64 и .NET 8 SDK. Сборка приложения не скачивает модели речи или текста.

```powershell
dotnet restore Egoist.Voice.sln
dotnet test Egoist.Voice.sln -c Release
dotnet publish Egoist.Voice.csproj -c Release -r win-x64 --self-contained true -o artifacts/app
```

Клиент общего движка перевода закреплён в `vendor/translation-client/1.0.0`.
Его бинарные файлы и SHA-256 включены в репозиторий. Для запуска перевода нужен сам движок
из Full, но для сборки приложения соседний проект Translator не нужен.

## Compact

`scripts/Build-CompactPortable.ps1` публикует редакцию `VoiceFlavor=Compact`, проверяет
четыре GigaAM-файла по каталогу и копирует их из локального кэша. Скрипт не скачивает модели.
Маркер `egoist-voice.portable` включает явные переносимые пути: `Models` и `Data` рядом с EXE.
Установщик строит `scripts/Build-CompactInstaller.ps1` по полному списку файлов и SHA-256.

```powershell
./scripts/Build-CompactPortable.ps1 -OutputDirectory "$PWD/artifacts/compact/portable-stage"
./scripts/Build-CompactInstaller.ps1 -StagingDirectory "$PWD/artifacts/compact/portable-stage" -Build
```

Перед публикацией добавьте актуальные лицензии в staging и обновите manifest. Выходная
папка должна быть чистой: не копируйте в неё Data, словарь, аудио или личные настройки.

## Full Preview 2

`scripts/Build-FullPreview.ps1` принимает явно подготовленные app staging, модели ASR,
Qwen и полный bundle Engine 1.0.1. По умолчанию показывает план; сборка включается `-Apply`.
Он проверяет модели и bundle по SHA-256, генерирует явный список Inno и делит результат
на части менее 2 ГиБ. Нужен закреплённый Inno Setup 6.2.1 из `.config/dotnet-tools.json`.

Full Preview 2 публикует проверенные байты EV-2224: app DLL совпадает с установленной
сборкой. Чистая сборка из исходников может иметь другой SHA из-за embedded build metadata;
публичный manifest связывает исходники, бинарные payload и результаты проверок.

Старый `scripts/build-installer.ps1` оставлен для воспроизведения исторического preview.1.
Для нового Full используйте `Build-FullPreview.ps1`; не запускайте старый установщик поверх
новой версии без проверки его политики данных.

## Проверка

`dotnet test` проверяет программные контракты. Native-команды `--local-asr-check` и
`--local-qwen-check` проверяют модели; в отчётах не сохраняются распознанные тексты.
Установщики проверяйте только в Windows Sandbox/VM: установка, запуск без сети, repair,
сохранение данных и удаление. На рабочей машине сборочный скрипт установщик не запускает.

Автоматические тесты не заменяют корпус записей, проверку микрофонов и матрицу GPU/Windows.
Не объявляйте стабильный релиз или общую точность на основании синтетических фраз.

## Russian 2.2.1 RC

`scripts/Build-RussianInstaller.ps1` создаёт автономный русский комплект: Compact/GigaAM,
Qwen3-4B Q4_K_M, llama.cpp b10219 CPU/Vulkan и self-contained .NET 8.0.30. Whisper,
Hy-MT и общий движок перевода не входят. Сначала восстановите Compact `win-x64`
с `-p:RuntimeFrameworkVersion=8.0.30`. Передайте явные локальные пути параметрам
`SpeechModelsRoot`, `TextModelPath`, `TextRuntimeZip`, `VcRuntimeDirectory` и собственный
временный `WorkDirectory`. `OutputDirectory` должен быть новым каталогом в `artifacts`.
Без `-Build` скрипт показывает план; с ним проверяет входные SHA-256, публикует приложение,
создаёт Inno payload и один внешний EXE с проверяемыми вложениями. Модели не скачиваются.

`russian-payload.manifest.json` содержит полный состав, SHA-256 и ревизию исходников;
`russian-installer.json` — размер, подпись и результат проверки встроенного пакета.
Свежая установка включает Qwen/автооформление и выключает историю аудио. Существующий
`Data/dictation.json` не заменяется и не удаляется деинсталлятором. Блокировку файлов
обрабатывает Windows Restart Manager; глобального `taskkill /IM` в этом установщике нет.
Сборка и проверка целостности не означают, что пройдены установка/обновление/удаление
в чистой Windows. Статус локального кандидата: [2.2.1-rc.1](releases/2.2.1-rc.1.md).
