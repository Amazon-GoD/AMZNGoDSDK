# Conditional Compilation System

## Обзор

Система условной компиляции для AmznGoDSDK позволяет полностью исключать отключенные модули из сборки проекта. Это уменьшает размер билда, сокращает время компиляции и удаляет ненужные зависимости.

## Как это работает

### 1. Define Symbols

Когда модуль включен в настройках SDK, система автоматически добавляет соответствующий define symbol:

| Модуль | Define Symbol |
|--------|---------------|
| SDK Enabled | `AMZN_SDK_ENABLED` |
| Adjust | `AMZN_ADJUST_ENABLED` |
| AppMetrica | `AMZN_APPMETRICA_ENABLED` |
| Cross Promo | `AMZN_CROSSPROMO_ENABLED` |
| In-App Purchase | `AMZN_IAP_ENABLED` |
| Firebase | `AMZN_FIREBASE_ENABLED` |
| Internet Connection | `AMZN_INTERNETCONNECTION_ENABLED` |
| In-Game Debug Console | `AMZN_DEBUGCONSOLE_ENABLED` |
| Analytics | `AMZN_ANALYTICS_ENABLED` |
| AppLovin MAX | `AMZN_APPLOVIN_ENABLED` |

### 2. Условная компиляция кода

Каждый модуль собирается в собственную assembly (asmdef) с
`defineConstraints: ["AMZN_<MODULE>_ENABLED"]` — при выключенном define сборка
модуля не компилируется целиком. `#if AMZN_*` остаются только в общем коде
(фасад `AmznGoDSDKCore`, межмодульные вызовы):

```csharp
#if AMZN_ADJUST_ENABLED
    // Код, зависящий от модуля Adjust
    _adjustModule.Initialize();
#endif
```

Folder-rename механика (переименование папок модулей в `~`) выведена из
эксплуатации в Фазе 3 UPM-перехода: в immutable-пакете перемещение папок
невозможно. Папки модулей всегда видимы; тогглы работают только через defines.

### 3. Управление зависимостями

- Нативные плагины (.jar/.aar/.so/.java/.mm) выключенных модулей исключаются из
  билда делегатами `PluginImporter.SetIncludeInBuildDelegate`
  (`NativePluginRegistry` + `NativePluginBuildFilter`).
- EDM4U-зависимости: шаблоны `*DependenciesTemplate.xml` внутри SDK мержатся
  генератором (`EdmDependencyGenerator`) в единый
  `Assets/AMZNGoDSDKGenerated/Editor/AmznGoDSdkDependencies.xml` — только для
  включённых модулей. Перегенерация происходит при каждом применении тогглов и
  перед билдом (`EdmDependencyBuildPreprocessor`).
- Внешние Firebase/MAX PluginImporter'ы также входят в фильтр выключенного
  модуля. Для MAX его UPM-пакеты и адаптеры удаляются из `Packages/manifest.json`
  с сохранением точных версий в `ProjectSettings`; при обратном включении они
  восстанавливаются.
- Firebase `*Dependencies.xml` при выключении обратимо получают нейтральное
  расширение, поэтому EDM4U не добавляет ни Android Maven artifacts, ни iOS Pods.
- `Resources` выключенного модуля запрещены: prefab InternetConnection создаётся
  в generated Resources только при включённом модуле, а `AppLovinSettings.asset`
  при выключении переносится под `Editor`.
- Перед сборкой `DisabledModuleBuildGuard` проверяет player assemblies и
  always-included Resources. После генерации Android-проекта последний guard
  удаляет оставшиеся Gradle/native следы и повторно сканирует результат. Любая
  недоказуемая очистка останавливает билд (`fail-closed`).
- `DisabledModuleSceneStripper` вырезает из временной копии build-сцен компоненты
  и prefab-инстансы выключенных модулей. Это не даёт ссылкам legacy
  `AmznGoDSDK.prefab` протащить UI/спрайты Cross-Promo или debug console.

## Использование

### Отключение модуля

1. Откройте **AMZN GoD > Settings**
2. Снимите галочку с модуля, который хотите отключить
3. Нажмите **Save Settings**
4. Unity автоматически перекомпилирует скрипты
5. Для AppLovin дождитесь завершения UPM Resolve; прежние версии пакетов будут
   восстановлены автоматически при следующем включении

### Проверка статуса модулей

Откройте **AMZN GoD > Module Status & Compilation Info** для просмотра:
- Какие модули активны
- Текущие define symbols
- Информацию о системе условной компиляции

## Компоненты системы

### ModuleDefineManager

Управляет scripting define symbols для всех build target groups.

**Основные методы:**
- `UpdateDefineSymbolsFromSettings()` - обновляет define symbols из настроек
- `IsModuleEnabled(string moduleDefine)` - проверяет, включен ли модуль
- `GetActiveModuleDefines()` - получает список активных defines

### NativePluginRegistry / NativePluginBuildFilter

Реестр «define модуля → папки нативных плагинов» и build-фильтр, который
вешает `SetIncludeInBuildDelegate` на PluginImporter'ы выключенных модулей.

### EdmDependencyGenerator / EdmDependencyBuildPreprocessor

Генерация сводного EDM4U Dependencies.xml из шаблонов включённых модулей
(в Assets потребителя, вне папки SDK — совместимо с immutable UPM-пакетом).

### DisabledModuleBuildGuard / DisabledModuleAndroidArtifactGuard

Проверяют инвариант «выключено = отсутствует в Player»: managed assemblies,
Resources, Gradle-зависимости и нативные файлы. Если артефакт нельзя убрать
безопасно, сборка завершается ошибкой с именем модуля и найденным следом.

### ModuleStatusWindow

Unity Editor окно для просмотра статуса модулей и debugging.

## Преимущества

✅ **Уменьшение размера билда** - неиспользуемый код не попадает в APK/IPA  
✅ **Быстрая компиляция** - меньше кода для компиляции  
✅ **Чистые зависимости** - только нужные библиотеки  
✅ **Гибкость** - легко включать/выключать модули  
✅ **Автоматизация** - система работает автоматически  

## Технические детали

### Поддерживаемые платформы

Система обновляет define symbols для:
- Android
- iOS
- Standalone (Windows/Mac/Linux)

### Файлы зависимостей

Шаблоны EDM-зависимостей (`*DependenciesTemplate.xml`) лежат внутри модулей и
перечислены в `EdmDependencyGenerator.Templates`; итоговый XML генерируется
в `Assets/AMZNGoDSDKGenerated/Editor/AmznGoDSdkDependencies.xml`.

### Автоматическое обновление

Define symbols обновляются автоматически при:
- Сохранении настроек SDK
- Загрузке Unity Editor
- Смене build target

## Примеры использования

### Проверка модуля в коде

```csharp
#if AMZN_ADJUST_ENABLED
    AmznGoDSDKCore.Instance.ReportEventAdjust("token", args);
#else
    Debug.Log("Adjust module is disabled");
#endif
```

### Условная зависимость

```csharp
public class MyAnalyticsManager
{
#if AMZN_APPMETRICA_ENABLED || AMZN_ADJUST_ENABLED
    public void TrackEvent(string eventName)
    {
        #if AMZN_APPMETRICA_ENABLED
        AmznGoDSDKCore.Instance.ReportEventAppMetrica(eventName, null);
        #endif
        
        #if AMZN_ADJUST_ENABLED
        AmznGoDSDKCore.Instance.ReportEventAdjust("token", null);
        #endif
    }
#endif
}
```

## Устранение проблем

### Define symbols не обновляются

1. Откройте Settings и сохраните их снова
2. Перезапустите Unity Editor
3. Вручную вызовите `ModuleDefineManager.UpdateDefineSymbolsFromSettings()`

### Модуль все еще компилируется

1. Проверьте активные defines в Module Status Window
2. Убедитесь, что галочка снята в Settings
3. Проверьте Player Settings > Scripting Define Symbols

### Ошибки компиляции после отключения

Если ваш код использует методы отключенного модуля, оберните его в условную компиляцию:

```csharp
#if AMZN_ADJUST_ENABLED
    // Ваш код использующий Adjust
#endif
```

## API Reference

### ModuleDefineManager

```csharp
// Обновить define symbols
ModuleDefineManager.UpdateDefineSymbolsFromSettings();

// Проверить, включен ли модуль
bool isAdjustEnabled = ModuleDefineManager.IsModuleEnabled(
    ModuleDefineManager.ADJUST_DEFINE
);

// Получить активные defines
List<string> activeDefines = ModuleDefineManager.GetActiveModuleDefines();
```

### Constants

```csharp
ModuleDefineManager.SDK_ENABLED_DEFINE           // "AMZN_SDK_ENABLED"
ModuleDefineManager.ADJUST_DEFINE                // "AMZN_ADJUST_ENABLED"
ModuleDefineManager.APPMETRICA_DEFINE            // "AMZN_APPMETRICA_ENABLED"
ModuleDefineManager.CROSSPROMO_DEFINE            // "AMZN_CROSSPROMO_ENABLED"
ModuleDefineManager.IAP_DEFINE                   // "AMZN_IAP_ENABLED"
ModuleDefineManager.FIREBASE_DEFINE              // "AMZN_FIREBASE_ENABLED"
ModuleDefineManager.INTERNETCONNECTION_DEFINE    // "AMZN_INTERNETCONNECTION_ENABLED"
ModuleDefineManager.DEBUGCONSOLE_DEFINE          // "AMZN_DEBUGCONSOLE_ENABLED"
ModuleDefineManager.ANALYTICS_DEFINE             // "AMZN_ANALYTICS_ENABLED"
ModuleDefineManager.APPLOVIN_DEFINE              // "AMZN_APPLOVIN_ENABLED"
```

## Лучшие практики

1. **Всегда используйте define symbols** для кода, зависящего от модулей
2. **Проверяйте Module Status** после изменения настроек
3. **Тестируйте билды** с разными комбинациями модулей
4. **Документируйте зависимости** между вашим кодом и модулями SDK

## Поддержка

При возникновении проблем:
1. Проверьте логи Unity Console (префикс `[AMZN GoD SDK]`)
2. Откройте Module Status Window для диагностики
3. Убедитесь, что все компоненты системы установлены

---

**Версия:** 1.0  
**Дата:** 2026  
**Совместимость:** Unity 2019.1+
