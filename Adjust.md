# Adjust

### Включение
- Убедитесь, что в `AMZN GoD/Settings` модуль Adjust включен и указан `Key` + окружение (`Production` или `Sandbox`).
- При сохранении настроек файл `Resources/amzn_god_sdk.json` обновляется, и `AmznGoDSDKCore` в рантайме подставит их в `AdjustModule`.

### Основной API
- `AmznGoDSDKCore.Instance.ReportEventAdjust(string token, Dictionary<string, string> args)` — отправляет событие в Adjust, добавляя все пары ключ/значение как `callback parameters`.
- Можно вызывать этот метод из любого места игры (например, при достижении уровня, покупке или установке).

### Советы
- События лучше по имени связать с `token` из Adjust dashboard (`Event token`).
- `args` может быть `null` или пустым словарём, если параметров нет.
- Adjust инициализируется автоматически в модуле `AdjustModule`, который живёт на `AmznGoDSDK` и не разрушается между сценами.





