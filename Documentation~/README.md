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
- `com.unity.ugui` and `com.unity.textmeshpro` are pulled in automatically as
  package dependencies.
- Firebase module only: the consumer project must contain the Firebase Unity
  SDK (Analytics/Crashlytics) — it is not bundled with this package.

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

## Module toggles

Open `AMZN GoD > SDK Settings`. Enabling/disabling a module:

- adds/removes the module's `AMZN_<MODULE>_ENABLED` scripting define — the
  module's assembly (asmdef with define constraints) compiles only when
  enabled;
- excludes the module's native plugins (.jar/.aar/.so) from builds while it is
  disabled;
- regenerates `Assets/AMZNGoDSDKGenerated/Editor/AmznGoDSdkDependencies.xml`
  so EDM4U resolves only the dependencies of enabled modules.

No files are moved or renamed inside the package — toggles are fully
compatible with the immutable UPM package cache.

## Migration from the .unitypackage distribution

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
