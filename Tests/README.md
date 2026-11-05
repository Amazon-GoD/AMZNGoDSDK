# AMZN GoD SDK — автотесты

EditMode-тесты (Unity Test Framework) основных фич SDK и pre-build gate, который не пускает билд с регрессией.

## Как это работает

- **Перед каждым билдом** (кнопка Build, one-click APK, CLI) `SdkTestsBuildGate` синхронно прогоняет все
  скомпилированные сборки `AMZNGoDSDK.*.Tests`. Хоть один красный тест — билд останавливается
  (`BuildFailedException` со списком упавших тестов). Прогон занимает секунды.
- **«Сохранить и собрать APK»** в SDK Settings прогоняет тесты на первом шаге, до установки зависимостей:
  красный тест останавливает сборку сразу, упавшие тесты видны в списке ошибок окна.
- **Выпуск SDK** — `Export SDK Package`, `Deploy SDK`, Release — останавливается при красном тесте.
  Пункт меню «Skip Pre-Build Tests» на выпуск не действует, обход только через `AMZN_SKIP_SDK_TESTS=1`.
  Если тестовая сборка не скомпилирована, выпуск тоже запрещён. Release собирается из HEAD, а тесты идут
  по рабочему дереву — коммить перед выпуском.
- **Вручную:** `AMZN GoD → Tests → Run SDK Tests` или `Window → General → Test Runner → EditMode`.
- **Без редактора (CI / перед коммитом):** редактор должен быть закрыт.

  ```
  powershell -ExecutionPolicy Bypass -File Assets/AMZNGoDSDK/Tests/run-sdk-tests.ps1
  ```

  Код выхода 0 — всё зелёное. Отчёт: `Logs/amzn-sdk-tests.xml`, лог: `Logs/amzn-sdk-tests.log`.
  Скрипт сохраняет и восстанавливает `Assets/Resources/amzn_god_sdk.json` (новая сессия редактора сбрасывает App Type).

- **Осознанный обход** (срочный билд при известном красном тесте): переменная окружения
  `AMZN_SKIP_SDK_TESTS=1` или `AMZN GoD → Tests → Skip Pre-Build Tests`. В Console будет warning.

Gate работает только в dev-проекте SDK: папка `Tests` не попадает в `.unitypackage`, а в UPM-пакете
тестовые сборки компилируются только при `testables`.

## Структура

| Папка | Сборка | Что покрыто |
|---|---|---|
| `Editor/` (корень) | `AMZNGoDSDK.Editor.Tests` | Console ID, first_open claim, профиль игры в first_open (1.0.11) |
| `Editor/Settings` | `AMZNGoDSDK.Editor.Tests` | конвертеры настроек editor↔runtime (round-trip всех полей), миграции, build guards (App Type, срок подписок), валидация IAP/баннеров, A/B-тесты Firebase и кодоген констант, `SdkJson` |
| `Editor/Dependencies` | `AMZNGoDSDK.Editor.Tests` | правка manifest.json (scopedRegistries), пины AppLovin, запрещённые сети |
| `Editor/BuildArtifacts` | `AMZNGoDSDK.Editor.Tests` | реестры модулей/артефактов/нативных плагинов, поиск APK/AAB для проверки |
| `Editor/Deploy` | `AMZNGoDSDK.Editor.Tests` | верификатор релизного дерева (в релиз не входит) |
| `Editor/Modules/<Module>` | `AMZNGoDSDK.Module.<Module>.Tests` | чистая логика модуля: Analytics, CrossPromo, Firebase, InAppPurchase |

Модульная тест-сборка имеет те же `defineConstraints`, что и модуль: выключенный модуль — его тесты не
компилируются и не запускаются.

## Правила для новых тестов

- Только `[Test]` / `[TestCase]` — gate запускает тесты синхронно, `[UnityTest]` будет пропущен.
- Не трогать реальный конфиг, `PlayerPrefs`, define-символы, `EditorPrefs`, `manifest.json` проекта.
  Файлы — только во временной папке с удалением в `[TearDown]`.
- Время — только явными параметрами, без `DateTime.UtcNow`.
- Для `internal` логики модуля: `AssemblyInfo.cs` модуля уже открывает internals его тест-сборке;
  `private static` → `internal static` с комментарием `// Internal for EditMode tests.`
- Регрессия конкретного бага — комментарий с версией из CHANGELOG над тестом.
