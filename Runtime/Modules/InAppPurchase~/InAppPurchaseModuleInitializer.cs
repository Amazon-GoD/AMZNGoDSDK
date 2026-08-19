#if AMZN_IAP_ENABLED
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InAppPurchaseModule))]
    public sealed class InAppPurchaseModuleInitializer : MonoBehaviour
    {
        [SerializeField] private int _bonusCoinsMultiplier = 10;
        [SerializeField] private int _energyAmount = 5;

        private InAppPurchaseModule _module;

        public void Initialize(InAppPurchaseModule module)
        {
            _module = module ?? GetComponent<InAppPurchaseModule>();
            if (_module == null || !_module.Enabled)
                return;

            _module.RegisterConsumableRewardType(
                ConsumableRewardType.BonusCoins,
                (rewardKey, amount) =>
                {
                    int current = PlayerPrefs.GetInt(rewardKey, 0);
                    PlayerPrefs.SetInt(rewardKey, current + amount * _bonusCoinsMultiplier);
                    PlayerPrefs.Save();
                });

            _module.RegisterConsumableRewardType(
                ConsumableRewardType.PremiumEnergy,
                (rewardKey, amount) =>
                {
                    int current = PlayerPrefs.GetInt(rewardKey, 0);
                    PlayerPrefs.SetInt(rewardKey, current + amount * _energyAmount);
                    PlayerPrefs.Save();
                });

            _module.SetConsumableRewardSetter((id, value) =>
            {
                int current = PlayerPrefs.GetInt(id, 0);
                PlayerPrefs.SetInt(id, current + value);
                PlayerPrefs.Save();
            });
        }
    }
}
#endif
