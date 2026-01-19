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
                    
                    Debug.Log("Коины");
                });

            _module.RegisterConsumableRewardType(
                ConsumableRewardType.PremiumEnergy,
                (rewardKey, amount) =>
                {
                    int current = PlayerPrefs.GetInt(rewardKey, 0);
                    PlayerPrefs.SetInt(rewardKey, current + amount * _energyAmount);
                    PlayerPrefs.Save();
                    
                    Debug.Log("Энергия");
                });
            
            _module.SetConsumableRewardSetter(((id, value) =>
            {
                if (PlayerPrefs.HasKey(id))
                {
                    var temp = PlayerPrefs.GetInt(id);
                    PlayerPrefs.SetInt(id, temp + value);
                    
                    Debug.Log("Дефолтный");
                }
            }));
        }
    }
}

