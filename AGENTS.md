# Egoist Voice — agent contract

Этот файл самодостаточен: сессия внутри проекта не должна зависеть от
`../../AGENTS.md` или от истории предыдущего чата.

## Scope and safety

- Наблюдаемый результат: Локальное Windows dictation app записывает микрофон по hotkey/mouse trigger, распознаёт речь on-device, нормализует текст и безопасно вставляет его в активное приложение.
- Работайте только внутри этого проекта и сохраняйте unrelated changes.
- Answer/review/diagnose/plan остаются read-only; build/change/fix разрешают
  локальные правки и соразмерную проверку.
- Не печатайте secrets, tokens, private data и содержимое пользовательских
  хранилищ. External, destructive, release и publish actions требуют явного
  запроса.

## Entry

1. Прочитайте только этот `STATUS.md`.
2. В новой сессии после `STATUS.md` откройте
   [`docs/KICKOFF.md`](./docs/KICKOFF.md) — он называет первый шаг и границы.
3. Откройте активную spec/ticket, нужные строки `docs/CONTEXT.md` и лишь
   указанного ими владельца истины.
3. Подтвердите факты source/manifests/tests или свежей командой.
4. Не читайте статусы других проектов и не создавайте status выше этого корня.

## Context loadout

- Active implementation: `STATUS.md` → active ticket → relevant
  `docs/CONTEXT.md` rows → named source/tests.
- ASR/corpus work: additionally load `EV-2201`–`EV-2204` owner selected by
  `STATUS.md`; do not preload the full corpus archive.
- Shared translation work: load `EV-2206` plus the current Translator handoff
  linked from the latest Voice change note; do not copy its project status.
- Release/installer work: load `EV-2209`, `EV-2210`, `docs/ROADMAP.md` and the
  latest relevant release evidence only.

## Commands

```powershell
dotnet build .\Egoist.Voice.sln -c Release
dotnet test .\Egoist.Voice.sln -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

## Continuity checkpoint

После каждого авторизованного write-task:

Непосредственно перед пунктом 1 перечитайте `STATUS.md` и
`docs/changes/INDEX.md`. Если другой writer уже продвинул их, объедините
новые факты и не стирайте его note/checks/blockers/next action.

1. Замените текущий снимок в `STATUS.md`; историю туда не дописывайте.
2. Обновите `docs/ARCHITECTURE.md`, `APP_MAP.md`, `ROADMAP.md`, specs,
   tickets или decisions только при изменении соответствующих фактов.
3. Добавьте одну immutable note в `docs/changes/` с UTC-именем: что, зачем,
   как, проверки, влияние на контракты, риски, файлы и следующий шаг.
4. Для выпущенной версии добавьте отдельный `docs/releases/<version>.md`.
5. Выполните
   `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\manage-project-history.ps1 -ProjectPath . -Mode Apply -KeepRecent 10`.

Read-only задачи bookkeeping-файлы не меняют. Параллельные writers используют
разные worktrees или отдельные копии и согласуют `STATUS.md` при объединении.

## Project gotchas

- Первым делом выполните [`EV-2200`](./docs/tickets/EV-2200-repository-safety-net.md):
  зафиксируйте текущие изменения в ветке `v2.2-wip`. Пользователь поручил это
  2026-08-02. `main` и линия 2.1.0 не трогаются; коммит в ветку не является
  релизом, а тег/упаковка/публикация по-прежнему требуют отдельного запроса.
- Порядок фаз до финальных установщиков —
  [`PROGRAM-PLAN.md`](../egoist-translator/docs/program/PROGRAM-PLAN.md);
  указатель на межпроектные документы — [`docs/program/README.md`](./docs/program/README.md).
- Установщик проверяется только в Windows Sandbox и Hyper-V VM, никогда на
  рабочей машине пользователя.
- Деинсталлятор Voice удаляет только `owners\egoist-voice.owner.json` и
  никогда чужой owner-файл или каталог движка при оставшемся владельце.
- Диктовка обязана работать при любом состоянии движка перевода, включая его
  полное отсутствие.
- `.ps1` не считается пройденной, если её не запускали.
- Preserve the dirty 2.1.1 candidate as tested baseline; фиксация в `v2.2-wip` —
  разрешённый способ его сохранить. Tag/push/publish только по явному запросу.
- Application and diagnostics must not log audio or recognized text.
- Port 47821 identity must confirm HY-MT before translation; sensitive-target delivery remains fail-closed.
