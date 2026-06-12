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
| Analytics | `AMZN_ANALYTICS_ENABLED` |

### 2. Условная компиляция кода

Код модулей обернут в директивы условной компиляции:

```csharp
#if AMZN_ADJUST_ENABLED
    // Код модуля Adjust
    _adjustModule.Initialize();
#endif
```

Если модуль отключен, этот код не компилируется и не попадает в билд.

### 3. Управление зависимостями

При сборке проекта система:
- Временно отключает Dependencies.xml файлы для неактивных модулей
- Это предотвращает загрузку Android/iOS зависимостей
- После сборки файлы восстанавливаются автоматически

## Использование

### Отключение модуля

1. Откройте **AMZN GoD > Settings**
2. Снимите галочку с модуля, который хотите отключить
3. Нажмите **Save Settings**
4. Unity автоматически перекомпилирует скрипты

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

### DependencyPreprocessor

Build preprocessor, который управляет файлами зависимостей.

**Что делает:**
- `OnPreprocessBuild` - отключает Dependencies.xml для неактивных модулей
- `OnPostprocessBuild` - восстанавливает файлы после сборки

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

Управляемые Dependencies.xml:
- `Assets/AMZNGoDSDK/Runtime/Modules/Adjust/Adjust/Native/Editor/Dependencies.xml`
- `Assets/AMZNGoDSDK/Editor/Modules/Appmetrica/Editor/AppMetricaDependencies.xml`

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
ModuleDefineManager.ANALYTICS_DEFINE             // "AMZN_ANALYTICS_ENABLED"
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
