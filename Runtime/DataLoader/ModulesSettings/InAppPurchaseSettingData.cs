using System;
using System.Collections.Generic;

namespace AMZNGoDSDK.Runtime
{
    [Serializable]
    public class InAppPurchaseSettingData : ModuleSettingData
    {
        public List<SubscriptionProduct> SubscriptionProducts = new();
        public List<ConsumableProduct> ConsumableProducts = new();
        public List<NonConsumableProduct> NonConsumableProducts = new();
    }

    /// <summary>
    /// Награды из настроек убраны (ТЗ IAP-13): SDK ничего не начисляет, он сообщает события
    /// и состояние, начисляет игра. Поля наград старых конфигов JsonUtility молча
    /// игнорирует — чтение не ломается.
    /// </summary>
    [Serializable]
    public class SubscriptionProduct
    {
        public string ProductId;
        public string DisplayName;

        /// <summary>
        /// Срок оплаченного периода в днях. 0 = «не задано»: Save Settings и билд
        /// останавливаются (IAP-15), рантайм периоды не начисляет. Молчаливых подстановок
        /// (Math.Max(1, …)) не делаем: они и превратили «не задано» в неверные 30 дней
        /// на портфеле, где все подписки недельные.
        /// </summary>
        public int TermDays = 0;

        /// <summary>
        /// ТЕСТОВЫЙ срок периода в минутах: при > 0 заменяет TermDays в расчёте периодов
        /// (10 = «подписка на 10 минут»). Нужен для проверки продлений реальным временем:
        /// у Amazon минимальный период Weekly, но продления в чеках не видны и периоды
        /// считает сам SDK — короткий локальный срок даёт честный прогон полного пути за
        /// минуты. TermDays остаётся обязательным (IAP-15). В прод-конфиге обязан быть 0 —
        /// рантайм ругается в лог при каждом Configure.
        /// </summary>
        public int TestTermMinutes = 0;

        // Устаревшее: от него считался локальный срок доступа, которым теперь управляет чек
        // Amazon (IAP-02). Оставлено, чтобы старые конфиги читались; убрать в следующем релизе.
        [Obsolete("Доступом управляет чек Amazon; срок периода — TermDays")]
        public int DurationDays = 30;

        public bool Enabled = true;
    }

    [Serializable]
    public class ConsumableProduct
    {
        public string ProductId;
        public string DisplayName;
        public bool Enabled = true;
    }

    /// <summary>
    /// Разовая покупка (ENTITLED у Amazon): убрать рекламу, разовый анлок, премиум-версия.
    /// Право выдаётся один раз и не теряется — кроме возврата денег (чек с CancelDate).
    /// Третий тип товара, IAP-05: раньше такие чеки сгорали без выдачи.
    /// </summary>
    [Serializable]
    public class NonConsumableProduct
    {
        public string ProductId;
        public string DisplayName;
        public bool Enabled = true;
    }
}
