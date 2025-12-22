# In-App Purchase Module

Модуль для работы с внутриигровыми покупками, включая подписки и единичные товары.

## Основные возможности

- **Поддержка подписок** - автоматическое отслеживание статуса подписки
- **Единичные товары** - покупка consumable товаров
- **Amazon Appstore & Google Play** - поддержка разных магазинов приложений
- **Fake Store для Editor** - тестирование в Unity Editor
- **Автоматическое начисление наград** - за подписку и покупки

## Использование

### Проверка статуса подписки
```csharp
// Проверить, активна ли подписка
bool isSubscribed = AmznGoDSDK.Instance.IsSubscribed("your_subscription_product_id");

// Если подписка активна, предоставить премиум контент
if (isSubscribed)
{
    // Показать премиум контент
}
```

### Покупка товара
```csharp
// Купить подписку или товар
AmznGoDSDK.Instance.BuyProduct("your_product_id");

// Подписаться на события
AmznGoDSDK.Instance.SetIAPPurchaseCompleteCallback((productId) =>
{
    Debug.Log($"Покупка завершена: {productId}");
});

AmznGoDSDK.Instance.SetIAPPurchaseFailedCallback((productId) =>
{
    Debug.Log($"Покупка не удалась: {productId}");
});
```

### Настройка в Editor

1. Откройте `AMZN GoD/Settings` в меню Unity
2. Включите модуль **In-App Purchase**
3. Выберите целевой магазин (Amazon Appstore или Google Play)
4. Настройте продукты:
   - **Subscription Products** - подписки с автоматическим возобновлением
   - **Consumable Products** - единичные товары

### Product ID форматы

- **Amazon Appstore**: Используйте Product ID из Amazon Developer Console
- **Google Play**: Используйте Product ID из Google Play Console

### Награды

Модуль автоматически начисляет монеты (`TotalCoins` в PlayerPrefs) при:
- Первой покупке подписки
- Покупке consumable товара

Количество монет настраивается в Editor для каждого продукта.

### Тестирование

- В Editor используется Fake Store для тестирования
- На устройстве тестируйте с реальными Product ID
- Подписки проверяются на истечение каждые 7 дней
