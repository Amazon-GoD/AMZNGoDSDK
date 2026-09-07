# Cross-Promo Banner

This sample is optional. The SDKPrefab sample starts the SDK and the Cross-Promo
manager without adding an advertising banner.

1. Import **SDKPrefab** and add `AmznGoDSDK.prefab` to the startup scene.
2. Enable **Cross-Promo** in SDK Settings and set its configuration URL.
3. Import **CrossPromoBanner** separately from the package's Samples tab.
4. Add `CrossPromoBanner.prefab` under a UI Canvas in the scene where you want
   the banner. Adjust its RectTransform to fit your layout.

The banner connects to the existing SDK manager, including when it is added
after SDK initialization.
It does not create another SDK or manager and is not added to other scenes
automatically. The SDK prefab provides the EventSystem required for UI clicks.

Use `AmznGoDSDKCore.Instance.SetCrossPromoBannerFuncs(onClose, isNoAds)` to
provide callbacks and the no-ads predicate. The sample's close button hides the
banner. Remove the banner from the scene when it is not needed.

The banner requires the Cross-Promo module to be enabled.
