# AMZN GoD SDK

A unified interface for managing all essential SDKs — Analytics, Advertising,
Cross-Promo and In-App Purchases — in Amazon Appstore projects.

## Requirements

- Unity **2022.3 LTS** or newer.
- Android build support (the SDK targets Amazon Appstore devices).
- [External Dependency Manager for Unity (EDM4U)](https://github.com/googlesamples/unity-jar-resolver)
  installed in the consumer project — it resolves the Android/iOS dependencies
  that the SDK generates. Install it via UPM git URL:
  `https://github.com/googlesamples/unity-jar-resolver.git?path=upm`.
- `com.unity.ugui`, `com.unity.textmeshpro` and the required built-in engine
  modules (`androidjni`, `imageconversion`, `unitywebrequest`,
  `unitywebrequesttexture`, `jsonserialize`, `video`) are pulled in
  automatically as package
  dependencies.
- Firebase module only: the consumer project must contain the Firebase Unity
  SDK (Analytics/Crashlytics) — it is not bundled with this package. Install
  it **before** enabling the module: without it the SDK refuses to set the
  define (console warning `dependencies are missing — skipping define`), and
  forcing `AMZN_FIREBASE_ENABLED` manually fails compilation with
  `CS0246: 'Firebase' could not be found`.

## Installation

Add the package to `Packages/manifest.json` (or through
`Window > Package Manager > + > Add package from git URL...`):

```json
{
  "dependencies": {
    "com.amzngod.amzngodsdk": "https://github.com/Amazon-GoD/AMZNGoDSDK.git#Releases"
  }
}
```

- `#Releases` — always the latest release.
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

### Build-time notes

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
