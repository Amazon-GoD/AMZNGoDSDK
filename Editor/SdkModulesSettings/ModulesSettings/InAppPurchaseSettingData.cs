using System;
using System.Collections.Generic;

namespace AMZNGoDSDK.Editor
{
    // Зеркало Runtime-модели. Типы намеренно раздельные (см. SdkSettingsManager-конвертеры):
    // поле, добавленное только в одну модель, молча теряется при сохранении — любые правки
    // делать в ОБЕИХ моделях и ОБОИХ конвертерах одним коммитом.
    //
    // Награды из настроек убраны (ТЗ IAP-13): SDK ничего не начисляет — он сообщает события
    // и состояние, начисляет игра.

    [Serializable]
    public class InAppPurchaseSettingData : ModuleSettingData
    {
        public List<SubscriptionProduct> SubscriptionProducts = new();
        public List<ConsumableProduct> ConsumableProducts = new();
        public List<NonConsumableProduct> NonConsumableProducts = new();
    }

    [Serializable]
    public class SubscriptionProduct
    {
        public string ProductId;
        public string DisplayName;

        /// <summary>Срок оплаченного периода в днях. 0 = «не задано» — Save Settings и билд остановятся (IAP-15).</summary>
        public int TermDays = 0;

        public bool Enabled = true;
    }

    [Serializable]
    public class ConsumableProduct
    {
        public string ProductId;
        public string DisplayName;
        public bool Enabled = true;
    }

    /// <summary>Разовая покупка (ENTITLED): право выдаётся один раз и не теряется, кроме возврата денег (IAP-05).</summary>
    [Serializable]
    public class NonConsumableProduct
    {
        public string ProductId;
        public string DisplayName;
        public bool Enabled = true;
    }
}
