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

        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public override void Cleanup() { }
    }
}
#endif
