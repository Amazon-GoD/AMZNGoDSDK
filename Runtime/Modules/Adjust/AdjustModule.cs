#if AMZN_ADJUST_ENABLED
using System;
using AdjustSdk;
using System.Collections.Generic;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public class AdjustModule : ModuleBase
    {
        private string _adjustKey;
        private AdjustSdk.AdjustEnvironment _environment;

        public void Construct(bool enable, string adjustKey, AdjustSdk.AdjustEnvironment environment)
        {
            Enabled = enable;
            _adjustKey = adjustKey;
            _environment = environment;
        }

        public override void Initialize()
        {
            if (string.IsNullOrWhiteSpace(_adjustKey))
            {
                Debug.LogError("[AMZNGoDSDK] Adjust app token is empty — SDK not initialized.");
                return;
            }

            var conf = new AdjustConfig(_adjustKey, _environment);
            Adjust.InitSdk(conf);
        }

        public void ReportEvent(string token, Dictionary<string, string> args)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                Debug.LogError("[AMZNGoDSDK] Adjust event token is empty.");
                return;
            }

            try
            {
                var adjustEvent = new AdjustEvent(token);

                if (args != null)
                {
                    foreach (var arg in args)
                    {
                        if (string.IsNullOrEmpty(arg.Key))
                            continue;
                        adjustEvent.AddCallbackParameter(arg.Key, arg.Value ?? string.Empty);
                    }
                }

                Adjust.TrackEvent(adjustEvent);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AMZNGoDSDK] Adjust event '{token}' failed: {ex.Message}");
            }
        }

        public override void Cleanup() { }
    }
}
#endif
