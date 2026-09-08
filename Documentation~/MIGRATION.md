# Миграция с поставки .unitypackage на UPM-пакет

Этот гайд — для проектов, куда AMZN GoD SDK ранее устанавливался как
`.unitypackage` (папка `Assets/AMZNGoDSDK`). Новый канал поставки — Unity
Package Manager по git URL из ветки `Releases`.

Проверено на Unity 2022.3.60f1 (минимальная поддерживаемая версия пакета —
2022.3).

## Требования перед миграцией

- **EDM4U** (External Dependency Manager for Unity) — обязателен: SDK генерирует
  файл зависимостей `Assets/AMZNGoDSDKGenerated/Editor/AmznGoDSdkDependencies.xml`,
  который резолвит именно EDM. Установка в `Packages/manifest.json`:

  ```json
  "com.google.external-dependency-manager": "https://github.com/googlesamples/unity-jar-resolver.git?path=upm"
  ```

- Доступ к приватному репозиторию `github.com/Amazon-GoD/AMZNGoDSDK` (SSH-ключ
  или PAT) на каждой машине, включая CI: UPM клонирует репозиторий git'ом.
- Android Build Support (SDK ориентирован на Amazon Appstore устройства).

## Шаги миграции

1. **Сохранённый конфиг не трогается.** Файл `Assets/Resources/amzn_god_sdk.json`
   лежит вне папки SDK и при миграции остаётся на месте — все настройки модулей,
   ключи и списки продуктов IAP сохранятся.
2. **Удалить `Assets/AMZNGoDSDK` целиком вместе с `AMZNGoDSDK.meta`**
   (проще всего — удалить папку в окне Project внутри Unity). Другие папки
   (`Assets/Resources`, ваш контент) не трогать.
3. **Добавить пакет** в `Packages/manifest.json` (или через
   `Window > Package Manager > + > Add package from git URL...`):

   ```json
   "com.amzngod.amzngodsdk": "https://github.com/Amazon-GoD/AMZNGoDSDK.git#Releases"
   ```

   `#Releases` — всегда последний релиз; `#vX.Y.Z` — фиксация конкретной версии.
4. **Ссылки в сценах и префабах выживают.** GUID всех ассетов SDK в пакете те же,
   что были в `Assets/AMZNGoDSDK`, поэтому ссылки на скрипты SDK и SDK-префаб в
   ваших сценах/префабах остаются рабочими после переезда файлов в
   `Packages/com.amzngod.amzngodsdk`.
5. **Открыть `AMZN GoD > SDK Settings` и один раз нажать Save** — это заново
   применит define-символы по вашему конфигу и перегенерирует
   `AmznGoDSdkDependencies.xml` для EDM.

## Что изменилось в механике тогглов

- Раньше выключенный модуль прятался переименованием папки (`Модуль` → `Модуль~`).
  В immutable UPM-пакете файлы двигать нельзя, поэтому **тогглы теперь работают
  только через scripting define symbols** (`AMZN_<MODULE>_ENABLED`):
  - код модуля собран в отдельный asmdef с define constraint — без define сборка
    просто не компилируется;
  - нативные плагины (.jar/.aar/.so/.java) выключенных модулей исключаются из
    Android-билда build-фильтром SDK;
  - сводный EDM-файл зависимостей содержит только включённые модули.
- Управление — по-прежнему `AMZN GoD > SDK Settings`; вручную defines трогать не
  нужно.

## Проекты со своими asmdef

Если ваш код лежит в собственных assembly definition и обращается к фасаду SDK
(`AmznGoDSDKCore` и типы `AMZNGoDSDK.Runtime`), добавьте в свой asmdef ссылку на
**`AMZNGoDSDK.Core`** (фасад переехал в отдельную сборку). Базовые типы и
утилиты остаются в `AMZNGoD.Runtime`. Проекты без собственных asmdef ничего не
меняют: обе сборки `autoReferenced`.

## Firebase

Модуль Firebase требует **Firebase Unity SDK в проекте потребителя**
(`Firebase.Analytics.dll`, `Firebase.Crashlytics.dll` в `Assets/...` — обычно
через импорт `FirebaseAnalytics.unitypackage`/`FirebaseCrashlytics.unitypackage`).
Пакет AMZN GoD SDK Firebase-библиотеки не бандлит.

Поведение проверено на стенде:

- Если включить Firebase в SDK Settings **без** установленного Firebase SDK,
  тоггл безопасен: SDK не выставит define и напишет в консоль
  `Module AMZN_FIREBASE_ENABLED is enabled but dependencies are missing —
  skipping define to prevent compilation errors.` Проект продолжает собираться.
- Если выставить define `AMZN_FIREBASE_ENABLED` руками без Firebase SDK —
  компиляция упадёт с `error CS0246: The type or namespace name 'Firebase'
  could not be found` в `FirebaseModule.cs`.

Порядок: **сначала** поставить Firebase Unity SDK, **потом** включать модуль.
Примечание: детект ищет Firebase-DLL в `Assets/**`; установка Firebase как
UPM-пакетов (`com.google.firebase.*`) детектом пока не распознаётся.

## Известные предупреждения и особенности

- **`link.xml` при выключенном Cross-Promo (IL2CPP).** `Runtime/link.xml` пакета
  сохраняет типы сборки `AMZNGoDSDK.Module.CrossPromo` от stripping. Когда
  Cross-Promo выключен, этой сборки нет, и IL2CPP-сборка может выдать
  предупреждение о неразрешённой сборке из link.xml — оно безвредно. В
  Mono-экспортах Android (проверенная матрица) предупреждение не воспроизводится.
- **Analytics App Type сбрасывается при каждом запуске Unity Editor** (защита от
  «унаследованного» free/paid). Перед каждым билдом с включённым Analytics нужно
  заново выбрать App Type в SDK Settings, иначе билд остановится с понятной
  ошибкой (`Analytics: не выбран App Type — сборка остановлена`).
- **Подписки IAP без Term (days)** также останавливают билд отдельным гардом —
  заполните срок каждой включённой подписки.
- Вес установки: UPM клонирует git-репозиторий целиком (порядка 150 MB) на
  каждую машину; это штатно.

---

# English summary — migrating from .unitypackage to UPM

1. Prerequisites: Unity 2022.3+, Android Build Support, **EDM4U**
   (`https://github.com/googlesamples/unity-jar-resolver.git?path=upm`), git
   access (SSH/PAT) to the private `Amazon-GoD/AMZNGoDSDK` repository.
2. Your config `Assets/Resources/amzn_god_sdk.json` lives outside the SDK
   folder and is preserved — do not delete it.
3. Delete `Assets/AMZNGoDSDK` together with `AMZNGoDSDK.meta`.
4. Add `"com.amzngod.amzngodsdk": "https://github.com/Amazon-GoD/AMZNGoDSDK.git#Releases"`
   (or `#vX.Y.Z`) to `Packages/manifest.json`.
5. Asset GUIDs are unchanged, so scene/prefab references to SDK scripts and the
   SDK prefab keep working.
6. Module toggles are defines-only now (`AMZN_<MODULE>_ENABLED` via
   `AMZN GoD > SDK Settings`); no folder renames happen inside the immutable
   package. Open SDK Settings and press Save once after migrating.
7. If your own asmdef uses the SDK facade, reference **`AMZNGoDSDK.Core`**.
8. Firebase: install the Firebase Unity SDK **before** enabling the module —
   otherwise the SDK skips the define with a console warning; forcing the
   define manually fails compilation with CS0246.
9. Known cosmetic warning: with Cross-Promo disabled, IL2CPP builds may warn
   about the `AMZNGoDSDK.Module.CrossPromo` assembly referenced from the
   package `link.xml`; Analytics App Type is intentionally reset on every
   Editor start and must be re-selected before building.
