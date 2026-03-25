#if AMZN_APPMETRICA_ENABLED
using Io.AppMetrica;
using System.Collections.Generic;
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

        public void ReportEvent(string eventName, Dictionary<string, string> args) => 
            AppMetrica.ReportEvent(eventName, JsonUtility.ToJson(args));

        public override void Cleenup() { }
    }
}
#endif
