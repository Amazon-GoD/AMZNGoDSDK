# In-App Purchase Module

Модуль для работы с внутриигровыми покупками через Amazon IAP V2 SDK, включая подписки и единичные товары.

## Основные возможности

- **Поддержка подписок** - автоматическое отслеживание статуса подписки
- **Единичные товары** - покупка consumable товаров
- **Amazon Appstore** - нативная работа через Amazon IAP V2 SDK
- **Автоматическое начисление наград** - за подписку и покупки
- **Восстановление покупок** - через GetPurchaseUpdates
- **NotifyFulfillment** - автоматическое подтверждение выполнения покупки

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
