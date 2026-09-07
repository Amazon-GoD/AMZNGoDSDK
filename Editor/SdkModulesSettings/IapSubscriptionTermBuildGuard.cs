using System.Linq;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Роняет билд, если у включённой подписки не задан срок периода TermDays (ТЗ IAP-15).
    ///
    /// Гард нужен помимо валидации окна настроек: конфиг можно поправить руками или
    /// получить из гита мимо окна. Срок обязателен, потому что от него считаются
    /// оплаченные периоды подписки (IAP-14) — старый молчаливый дефолт в 30 дней на
    /// портфеле, где ВСЕ подписки недельные, и был источником потерь.
    ///
    /// Окон намеренно не показываем (билд может идти из CI) — по образцу
    /// <see cref="AnalyticsAppTypeBuildGuard"/>, но БЕЗ сброса на старте редактора:
    /// срок подписки между билдами не меняется, сбрасывать его каждый запуск незачем.
    ///
    /// Лежит в SdkModulesSettings, а не в Editor/Modules/InAppPurchase: ту папку тулинг
    /// оборачивает в #if AMZN_IAP_ENABLED, а гард, исчезающий из-за устаревшего дефайна,
    /// хуже отсутствующего.
    /// </summary>
    internal sealed class IapSubscriptionTermBuildGuard : IPreprocessBuildWithReport
    {
        // Раньше DependencyPreprocessor (-100): не даём билду начать раскладывать
        // зависимости ради заведомо провальной сборки.
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = SdkSettingsManager.LoadRuntimeSettings();

            // Конфига нет или SDK/модуль выключены — подписок в сборке не будет.
            if (settings == null || !settings.Enabled)
                return;
            if (settings.InAppPurchase == null || !settings.InAppPurchase.Enabled)
                return;

            // Только ВКЛЮЧЁННЫЕ подписки: выключенный продукт билд ронять не должен.
            var missing = settings.InAppPurchase.SubscriptionProducts
                .Where(p => p != null && p.Enabled && !string.IsNullOrWhiteSpace(p.ProductId) && p.TermDays <= 0)
                .Select(p => p.ProductId.Trim())
                .ToArray();

            if (missing.Length == 0)
                return;

            throw new BuildFailedException(
                "[AMZNGoDSDK] IAP: у подписок не задан срок периода — сборка остановлена: " +
                string.Join(", ", missing) + ".\n" +
                "Срок обязателен: от него считаются оплаченные периоды подписки (события о продлениях).\n" +
                "Как починить: AMZN GoD → SDK Settings → In-App Purchase → у каждой подписки заполнить " +
                "Term (days) реальным периодом из консоли Amazon (например, 7 для недельной) → Save Settings, " +
                "затем собрать заново."
            );
        }
    }
}
