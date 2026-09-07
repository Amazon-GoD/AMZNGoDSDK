#if AMZN_APPMETRICA_ENABLED
using Io.AppMetrica;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public class AppMetricaModule : ModuleBase
    {
        private string _appMetricaKey;

        public void Construct(bool enable, string appMetricaKey)
        {
            Enabled = enable;
            _appMetricaKey = appMetricaKey;
        }

        public override void Initialize() =>
            AppMetrica.Activate(new AppMetricaConfig(_appMetricaKey));

        public void ReportEvent(string eventName, Dictionary<string, string> args)
        {
            if (string.IsNullOrWhiteSpace(eventName))
                return;

            if (args == null || args.Count == 0)
            {
                AppMetrica.ReportEvent(eventName);
                return;
            }

            AppMetrica.ReportEvent(eventName, SerializeToJson(args));
        }

        // Отправка сырого (в т.ч. вложенного) JSON как есть. Обёртка ReportEvent(Dictionary)
        // строит только ПЛОСКИЙ JSON через SerializeToJson — деревом параметров (воронка IAP,
        // sku → reason → network) её отправить нельзя. Плагин же принимает произвольный JSON.
        public void ReportEventRaw(string eventName, string jsonValue)
        {
            if (string.IsNullOrWhiteSpace(eventName))
                return;

            AppMetrica.ReportEvent(eventName, jsonValue);
        }

        private static string SerializeToJson(Dictionary<string, string> args)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kvp in args)
            {
                if (string.IsNullOrEmpty(kvp.Key))
                    continue;

                if (!first) sb.Append(',');
                sb.Append('"').Append(Escape(kvp.Key)).Append("\":\"").Append(Escape(kvp.Value ?? string.Empty)).Append('"');
                first = false;
            }
            sb.Append('}');
            return sb.ToString();
        }

        // Реализация уехала в общий SdkJson (ТЗ IAP-19): тот же эскейп нужен воронке IAP,
        // а держать две версии — значит снова получить урезанную копию в одной из них.
        private static string Escape(string s)
        {
            return SdkJson.Escape(s);
        }

        public override void Cleanup() { }
    }
}
#endif
