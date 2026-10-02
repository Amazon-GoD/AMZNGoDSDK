# AMZN GoD SDK

A unified interface for managing all essential SDKs — Analytics, Advertising,
Cross-Promo and In-App Purchases — in Amazon Appstore projects.

## Requirements

- Unity **2022.3 LTS** or newer and its Android Build Support module.
- Android **API 24** or newer when AppLovin MAX is enabled.
- Enable the required modules and save **AMZN GoD → SDK Settings**. The SDK
  automatically prepares EDM4U, MAX and its allowed Android adapters,
  Firebase for the enabled module, and the Android build tools. First setup
  needs an internet connection; progress and retry are available in SDK
  Settings. Wait for dependency preparation before building.
- Firebase and MAX are installed as external dependencies rather than
  embedded in this SDK package. See [Android dependencies](ANDROID-DEPENDENCIES.md)
  for pinned versions and toolchain requirements.
- Configure **AppLovin MAX → AdMob Android App ID** in SDK Settings before
  an Android build with MAX; a missing or invalid effective App ID blocks it.
- `com.unity.ugui`, `com.unity.textmeshpro` and the required built-in engine
  modules (`androidjni`, `imageconversion`, `unitywebrequest`,
  `unitywebrequesttexture`, `jsonserialize`, `video`) are installed as
  package dependencies.

## Installation

Add the package to `Packages/manifest.json` (or through
`Window > Package Manager > + > Add package from git URL...`):

```json
{
  "dependencies": {
    "com.amzngod.amzngodsdk": "https://github.com/Amazon-GoD/AMZNGoDSDK.git#beta"
  }
}
```

- `#beta` — the latest beta package; this snapshot is **1.0.8-beta.2**.
- `#v1.0.8-beta.2` — pin this exact beta version.
- `#Releases` — the stable release channel.
- `#vX.Y.Z` (e.g. `#v1.0.0`) — pin an exact release version.

The repository is private: access requires GitHub credentials (SSH key or PAT)
on every machine and on CI.

After the first import the **Setup Wizard** (`AMZN GoD > Setup Wizard`) opens
and guides you through the initial configuration. The configuration file is
stored in your project at `Assets/Resources/amzn_god_sdk.json` and is never
touched by package updates.

### SDK prefab (sample)

The configured SDK prefab ships as a package sample: open
`Window > Package Manager`, select **AMZN GoD SDK**, expand **Samples** and
import **SDKPrefab**. Drop `AmznGoDSDK.prefab` into your boot scene.

The sample prefab contains only the permanent SDK core and Unity's neutral
`EventSystem` components. Optional module components are attached at runtime
only when their `AMZN_<MODULE>_ENABLED` define is compiled. This makes it safe
to keep the same prefab in a scene while modules are switched on and off.

## Module toggles

Open `AMZN GoD > SDK Settings`. Enabling/disabling a module:

- adds/removes the module's `AMZN_<MODULE>_ENABLED` scripting define — the
  module's assembly (asmdef with define constraints) compiles only when
  enabled;
- excludes the module's native plugins (.jar/.aar/.so) from builds while it is
  disabled;
- regenerates `Assets/AMZNGoDSDKGenerated/Editor/AmznGoDSdkDependencies.xml`
  so EDM4U resolves only the dependencies of enabled modules.
- creates module-owned UI prefabs under
  `Assets/AMZNGoDSDKGenerated/Resources/AMZNGoDSDK` only while the owning
  assembly is enabled. Disabling InternetConnection, Cross-Promo or
  InGameDebugConsole removes its generated prefab before the define is
  removed, preventing stale serialized components and excluding the resource
  from the Player. If none remain, the generated `Resources` folder is removed
  as well.

No files are moved or renamed inside the package — toggles are fully
compatible with the immutable UPM package cache.

EDM4U picks up the generated dependencies file automatically: on the first
Android resolve it enables `Assets/Plugins/Android/mainTemplate.gradle` (plus
`gradleTemplate.properties` / `settingsTemplate.gradle`) in the consumer
project and injects the Maven dependencies of the enabled modules there.

### AppLovin AD Placements

In `AMZN GoD > SDK Settings > AppLovin > AD Placements`, set separate
Interstitial and Rewarded placement names for MAX reporting. Save Settings
stores them in the SDK config and uses them for MAX shows. These names are
separate from the Ad Unit IDs and do not change `placement` in SDK events or
the SDK's native Adjust/AppMetrica revenue reports.

Empty values and older configs use `interstitial` and `rewarded` respectively.
Leading and trailing whitespace is removed.

### Advertising event fields

`placement` identifies the kind of advertising location in all SDK-generated
cross-promo and MAX events: `interstitial`, `rewarded`, or `banner`. It is
determined by the actual show/callback path, including requests, no-fill and
errors. Configurable MAX placement names cannot override these values.
`network_placement` remains the mediated network's identifier in MAX custom
events; `ad_unit` remains its ad unit ID.

Backend `cp_impression`, `cp_click`, `mediation_impression`, and
`mediation_click` use the same placement values as custom events. Native MAX
revenue reports use them in Adjust `AdRevenuePlacement` and AppMetrica
`AdPlacementName`; AppMetrica also receives the matching `AdType`, including
`Banner`. No additional format field is sent. MAX banners use the fixed
placement `banner`, set in MAX immediately after banner creation.

MAX banner clicks emit `mediation_banner_clicked` and backend
`mediation_click`. Each banner revenue callback, including auto-refreshes,
emits `mediation_banner_displayed`, `mediation_ad_revenue`, backend
`mediation_impression`, and native revenue reports. Loading, showing or hiding
the banner does not synthesize an impression. Late revenue after hiding is
retained. Load errors emit `mediation_banner_load_failed` in AppMetrica with
`placement`, `ad_unit`, `reason`, and `error_code` when available.
Displayed/clicked custom events follow the existing AppMetrica/Adjust routing.
Banner callbacks for other ad unit IDs are ignored.

Public tracking methods retain their original signatures. When calling them
manually, pass `interstitial`, `rewarded`, or `banner` as `placement`. Legacy
cross-promo calls without a known placement still omit the field; mediation
retains its existing string field (`null` becomes `""`). Unknown sources are
not automatically classified as interstitial.

For example, a MAX rewarded click includes the following, regardless of the
MAX placement name configured for its show:

```json
{
  "event_name": "mediation_click",
  "placement": "rewarded"
}
```

This is a field excerpt; other existing event fields are unchanged. Aggregate
cross-promo and MAX events by `placement`. `first_open` and Adjust tracking
URLs are unchanged.

MAX API references: [banner callbacks](https://support.applovin.com/en/max/unity/ad-formats/banner-and-mrec-ads)
and [placement configuration](https://support.applovin.com/en/max/unity/overview/advanced-settings).

### Build-time notes

The current dependency upgrade retains Unity 2022.3 and supports Android API
24. Enabled module dependencies and Android build tools are prepared
automatically. See [Android dependencies](ANDROID-DEPENDENCIES.md) for pinned
versions, installation behavior and verification limits.

- **Analytics App Type is reset on every Unity Editor start** (so a paid build
  cannot silently inherit yesterday's free type). Re-select it in
  `AMZN GoD > SDK Settings` before building — otherwise the build stops with a
  clear error message.
- Enabled IAP subscriptions must have `Term (days)` set, or the build stops.
- With Cross-Promo disabled, IL2CPP builds may log a harmless warning about
  the `AMZNGoDSDK.Module.CrossPromo` assembly referenced from the package
  `link.xml`.

## Verified configurations

Android gradle exports from a clean Unity 2022.3.60f1 consumer project with
the package installed from the `Releases` branch and EDM4U 1.2.187 (verified
2026-08-23):

| Configuration | Result |
|---|---|
| All modules ON except Firebase | Export OK: IAP jars/.so, UniWebView.aar, ExoPlayer bridge sources, AppMetrica Java bridge, IngameDebugConsole.aar present; IAP receivers + bootstrap provider injected into the manifest; EDM file lists adjust / installreferrer / appmetrica / exoplayer |
| IAP OFF (rest ON) | Export OK: 9 IAP natives excluded, no Amazon IAP manifest entries |
| Cross-Promo OFF (rest ON) | Export OK: 7 CP natives excluded (incl. the module's AndroidManifest.xml), no CP manifest entries, no exoplayer in EDM file |
| All modules OFF | Export OK: only `unity-classes.jar`, EDM file removed, manifest clean |
| Firebase ON without Firebase SDK | Define skipped with a console warning (project keeps compiling); forcing the define fails compilation with CS0246 as designed |

The all-off transition was additionally rechecked on 2026-09-04 after the
prefab isolation change: clean import and post-toggle domain reload complete
without `Scripted Object has unknown format`, prefab-layout or missing-script
errors; no optional module assemblies or generated module resources remain.

The runtime IAP purchase flow on a real Amazon device is **not** covered by
these checks and must be verified on hardware.

## Migration from the .unitypackage distribution

See `Documentation~/MIGRATION.md` for the full guide (RU + EN). Short version:

1. Save your configuration file `Assets/Resources/amzn_god_sdk.json`
   (it lives outside the SDK folder and is normally not affected).
2. Delete the folder `Assets/AMZNGoDSDK` **together with its `.meta` files**
   (delete it from within Unity, or remove the folder and `AMZNGoDSDK.meta`).
3. Add the package via the git URL above.
4. Asset GUIDs are preserved, so existing scene/prefab references to SDK
   scripts and the SDK prefab keep working.
5. If your own asmdef referenced `AMZNGoD.Runtime` to reach
   `AmznGoDSDKCore`, add a reference to `AMZNGoDSDK.Core` (the facade moved to
   its own assembly).
6. Re-open `AMZN GoD > SDK Settings` and press Save once to re-apply defines
   and regenerate the EDM dependency file.

A detailed integration guide is maintained in the project documentation
(see `documentationUrl` in `package.json`).
