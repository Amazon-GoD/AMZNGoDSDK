namespace AMZNGoDSDK.Runtime
{
    public class FirebaseModule : ModuleBase
    {
        public void Construct(bool enable)
        {
            Enabled = enable;
        }

        public override void Initialize() { }

        public override void Cleenup() { }
    }
}

