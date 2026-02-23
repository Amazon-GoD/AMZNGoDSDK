#if AMZN_ADJUST_ENABLED
using AdjustSdk;
using System.Collections.Generic;

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
            var conf = new AdjustConfig(_adjustKey, _environment);
            Adjust.InitSdk(conf);
        }

        public void ReportEvent(string token, Dictionary<string, string> args)
        {
            var adjustEvent = new AdjustEvent(token);
            
            foreach (var arg in args)
            {
                adjustEvent.AddCallbackParameter(arg.Key, arg.Value);
            }
            
            Adjust.TrackEvent(adjustEvent);
        }

        public override void Cleenup() { }
    }
}
#endif
