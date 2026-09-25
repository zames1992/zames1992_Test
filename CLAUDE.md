# Hoodie Companion — правила проекта для Claude Code

Этот файл Claude Code читает автоматически в каждой сессии. Подробный вводный промпт лежит в
`HoodieCompanion/docs/MASTER_PROMPT.md`.

## Что это

Hoodie Companion — «живой» компаньон для рабочего стола Windows (x64, .NET 8, WPF). Маленький
персонаж в худи живёт на рабочем столе и одновременно служит интерфейсом к полезным функциям: рюкзак
с ярлыками, заметки, напоминания, таймер, статус ПК, воспоминания. Весь код лежит в `HoodieCompanion/`.

## Команды (Windows, из папки `HoodieCompanion`)

```powershell
dotnet test tests\HoodieCompanion.Tests -c Release       # юнит- и симуляционные тесты (сейчас 81, все должны проходить)
.\build-release.bat                                      # чистая сборка + тесты + publish\win-x64\HoodieCompanion.exe + zip
dotnet run --project src\HoodieCompanion -c Release      # запуск из исходников
publish\win-x64\HoodieCompanion.exe --qa qa --data qadata   # автоматический QA-сценарий (~2 мин) -> qa\qa-report.txt + скриншоты
publish\win-x64\HoodieCompanion.exe --render-poses poses.png --data posedata   # лист поз персонажа
publish\win-x64\HoodieCompanion.exe --data <папка> [--verbose]                # отдельная папка данных, подробный лог
python tools\gen_loc.py                                  # проверить переводы и сгенерировать UI\Loc.Ru.cs из tools\ru.json
$env:HOODIE_EXPORT_CATALOG="$PWD\ANIMATION_CATALOG.md"; dotnet test tests\HoodieCompanion.Tests -c Release   # обновить каталог анимаций
```

Данные пользователя: `%LocalAppData%\HoodieCompanion\` (settings/territory/inventory/notes/reminders/timers/memory.json, logs\).
Проверки на Windows в CI: `.github/workflows/hoodie-companion.yml` — сборка, тесты, QA-сценарий, проверка
единственного экземпляра.

## Архитектура (коротко)

- `src/HoodieCompanion.Core` (net8.0, без WPF и Win32, всё тестируется) — вся логика:
  - `Companion/Behavior`: `PetController` (partial: Movement, Life, Mind, Presence, Activities, Climb, Surfaces,
    Perception), `PetStateMachine`, `Mind` и `Personality`, `IntentSystem`, `BehaviorController`, `IdleDirector`,
    `ReactionSystem`.
  - `Companion/Perception` (восприятие), `Companion/Memory` (память и прогрессия), `Companion/Animation`
    (процедурные позы и 132 клипа), `Companion/Physics`, `Presence` (территория), `Features`, `Storage`.
- `src/HoodieCompanion` (WPF-оболочка, Win32):
  - `AppHost.cs` — связывает всё вместе, покадровый цикл (`OnFrame`) и служебный таймер раз в секунду.
  - `UI/`: окно персонажа, `QuickPanel`, `SettingsWindow`, редактор территории, трей, стикеры.
  - `Platform/`: мышь, мониторы и DPI, `SurfaceScanner` (верхние края окон и иконки), `WindowEvents`
    (SetWinEventHook), `PerformanceWatch`.
  - `QaRunner.cs` — автоматический QA-сценарий.
- Путь поведения: восприятие → Mind/Personality/Memory → намерение (Intent, с причиной) → действие →
  анимация → реакция. Подробно: `HoodieCompanion/PLAN.md`.
- XAML нет: всё интерфейсное строится кодом (стили в `UI/Ui.cs`).
- Персонаж — векторный rig (`Assets/rig.json`); внешность описана в `CHARACTER_BIBLE.md`.

## Непреложные правила

1. Контроль пользователя всегда выше автономии персонажа: территория, режимы, правила приложений, «оставь
   меня». Нарушение — это баг.
2. Приватность:
   - не хранить и не читать введённый текст, пароли, заголовки окон, документы, сообщения и буфер обмена;
   - не делать скриншоты без отдельного явного согласия;
   - всё обрабатывается локально, никакой сети и телеметрии;
   - экран «Приватность» в настройках должен оставаться точным.
3. Приоритеты продукта:
   1. не мешать пользователю;
   2. осмысленное, а не случайное поведение;
   3. видимая связь между событиями на ПК и реакцией персонажа;
   4. повод оставить приложение запущенным завтра.
   Только потом: прогрессия → инвентарь → коллекция → кастомизация.
4. Производительность: только событийная модель, спящие состояния, стабильная работа 8–12 часов без утечек.
   Следить за процессором, памятью, дескрипторами и GDI/USER-объектами (`PerformanceWatch`, строки `perf` в логе).
5. Любое новое поведение — с тестом в `tests/` (симуляция через `PetController.Update` с `PetInput`). Тест должен
   падать без исправления и проходить с ним.
6. Перед коммитом: все тесты зелёные, `python tools\gen_loc.py` без пропущенных строк (у каждой строки в
   `T("...")`/`F("...")` есть перевод в `tools/ru.json`), QA-сценарий `RESULT: PASS`.
7. Обновлять документацию: `PROGRESS.md`, `KNOWN_ISSUES.md`, `README.md`, `PLAN.md` (архитектура) и каталог
   анимаций, если менялись клипы.
8. Git:
   - рабочая ветка `claude/create-folder-f23ng6`, PR https://github.com/zames1992/zames1992_Test/pull/1;
   - осмысленные сообщения коммитов;
   - без force-push.
9. Общение с пользователем — по-русски; код, комментарии и документы в репозитории — по-английски
   (кроме этого файла и мастер-промпта).
