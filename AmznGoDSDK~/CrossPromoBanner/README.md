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

Runtime controls on `AmznGoDSDKCore.Instance`:

- `SetCrossPromoBannerEnabled(bool)` hides or restores the whole banner slot,
  including the AppLovin MAX banner that replaces the creative once the
  cross-promo caps are exhausted. The MAX banner is a native view above all
  Unity UI, so use this on screens where no banner may appear.
- `SetAppLovinBannerFallbackEnabled(bool)` keeps the cross-promo banner instead
  of switching to MAX.
- `SetAppLovinBannerPosition(AppLovinBannerPosition)` anchors the MAX banner to
  the top or bottom of the screen. Match it to where this prefab is placed.
- `CurrentBannerProvider` / `OnBannerProviderChanged` report what the slot
  shows: `None`, `CrossPromo` or `AppLovin`.

The banner requires the Cross-Promo module to be enabled.
