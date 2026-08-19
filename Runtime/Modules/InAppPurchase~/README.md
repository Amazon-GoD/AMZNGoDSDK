# In-App Purchase Module

Модуль для работы с внутриигровыми покупками через Amazon IAP V2 SDK, включая подписки и единичные товары.

## Основные возможности

- **Поддержка подписок** - автоматическое отслеживание статуса подписки
- **Единичные товары** - покупка consumable товаров
- **Amazon Appstore** - нативная работа через Amazon IAP V2 SDK
- **Автоматическое начисление наград** - за подписку и покупки
- **Восстановление покупок** - через GetPurchaseUpdates
- **NotifyFulfillment** - автоматическое подтверждение выполнения покупки

## Нативные зависимости (Plugins/Android)

| Файл | Что это |
|---|---|
| `amazon-appstore-sdk-3.0.9.jar` | Amazon Appstore SDK — IAP + DRM + Simple Sign-in |
| `AmazonIapV2Client.jar`, `AmazonIapV2JavaService-1.0.jar`, `AmazonCptPluginsUtils-1.0.jar`, `gson-2.2.4.jar`, `libs/*/libAmazonIapV2Bridge.so` | легаси Unity-плагин Amazon IAP V2 (JNI-мост, через который ходит C#) |
| `AmazonIapV2Compat.jar` | шим: возвращает удалённый в 3.0.9 класс `com.amazon.android.CrossPlatformPluginUtils`, без него легаси-плагин падает с `NoClassDefFoundError` при инициализации. Исходник и обоснование — в `AmazonIapV2Compat~/` |

Манифест: `com.amazon.device.iap.ResponseReceiver` (и DRM-аналог) объявлены в глобальном
`Assets/Plugins/Android/AndroidManifest.xml` — без них Appstore не доставляет ответы
`PurchasingListener`. С выключенным модулем их вырезает `DisabledModuleManifestCleaner`.

**Ограничение:** `libAmazonIapV2Bridge.so` собран только под 32-бит (`armeabi-v7a`, `armeabi`, `x86`) —
arm64-сборка с этим плагином работать не будет. Проект сейчас собирается под ARMv7
(`AndroidTargetArchitectures: 1`). Для перехода на arm64 легаси-плагин надо заменить прямыми
вызовами `com.amazon.device.iap.PurchasingService` через `AndroidJavaClass`/`AndroidJavaProxy`.

## Использование

### Проверка статуса подписки
```csharp
bool isSubscribed = AmznGoDSDK.Instance.IsSubscribed("your_subscription_product_id");

if (isSubscribed)
{
    // Показать премиум контент
}
```

### Покупка товара
```csharp
AmznGoDSDK.Instance.BuyProduct("your_product_id");

AmznGoDSDK.Instance.SetIAPPurchaseCompleteCallback((productId) =>
{
    Debug.Log($"Покупка завершена: {productId}");
});

AmznGoDSDK.Instance.SetIAPPurchaseFailedCallback((productId) =>
{
    Debug.Log($"Покупка не удалась: {productId}");
});
```

### Восстановление покупок
```csharp
AmznGoDSDK.Instance.RestorePurchases((success) =>
{
    Debug.Log($"Восстановление: {(success ? "успешно" : "ошибка")}");
});
```

### Настройка в Editor

1. Откройте `AMZN GoD/Settings` в меню Unity
2. Включите модуль **In-App Purchase**
3. Настройте продукты:
   - **Subscription Products** - подписки
   - **Consumable Products** - единичные товары

### Product ID форматы

Используйте Product ID (SKU) из Amazon Developer Console.

### Награды

Модуль автоматически начисляет монеты (`TotalCoins` в PlayerPrefs) при:
- Первой покупке подписки
- Покупке consumable товара

Количество монет настраивается в Editor для каждого продукта.

### Тестирование

- Используйте Amazon App Tester для тестирования на устройстве
- В Editor используется stub-реализация Amazon SDK
- Подписки проверяются через GetPurchaseUpdates
