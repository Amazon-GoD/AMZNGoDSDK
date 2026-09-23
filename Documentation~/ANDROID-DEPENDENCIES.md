# Android dependencies — 2026-09-23

Unity remains **2022.3 LTS** (development project: 2022.3.60f1). The updated
mediation requires **Android API 24** on the device. Compilation uses API 36;
this is independent of the application's target API, which this upgrade does
not change.

## Installation

Enable the required modules and save SDK Settings. The SDK prepares EDM4U,
MAX and its allowed Android adapters, Firebase, and Android build tools
automatically. Initial installation needs an internet connection. Progress
and retry are available in **AMZN GoD → SDK Settings**. Interrupted or failed
package operations retain backups and report the failure instead of retrying
on every script reload.

The Android tool installer reuses compatible local tools when available.
Otherwise it downloads JDK 17 from Eclipse Adoptium, Gradle 8.13 from Gradle,
and Android Platform 36 / Build Tools 36.0.0 from Google. Downloads are checked
against published checksums. Downloaded tools live in
`Library/AmznGoDSDK/AndroidToolchain`; deleting Library requires preparation
again. No machine-specific paths are committed to the SDK.

Unity's JDK 11, Android SDK and NDK stay in place. Generated Gradle projects
use a separate JDK 17 and Android SDK, AGP **8.13.2**, and the prepared
Gradle **8.13**. The previous Gradle selection is restored after building.
The SDK automatically raises Minimum API Level to 24 when MAX is enabled;
a project's higher minimum is retained. Known old R8 8.2.47 and Kotlin 1.8.22
overrides are removed from generated projects.

CI can optionally supply `AMZNGOD_ANDROID_JAVA_HOME`,
`AMZNGOD_ANDROID_SDK_ROOT`, and `AMZNGOD_ANDROID_GRADLE_HOME`. Ordinary
installation does not require these variables or manual path selection.
Unity's Android Build Support module must already be installed with Unity.

## Updated modules

| Component | Previous | Updated |
|---|---|---|
| MAX Unity / Android | 8.6.3 / 13.6.2 | 8.6.6 / 13.6.4 |
| Firebase Unity | 12.8.0 | 13.17.0 |
| Firebase Analytics | 22.4.0 | 23.2.0 |
| Firebase Remote Config | 22.1.0 | 23.1.0 |
| Firebase Crashlytics / NDK | 19.4.2 | 20.1.1 |
| Firebase Common | 21.0.0 | 22.2.1 |
| Adjust Unity / Android / iOS | 5.4.4 / 5.4.5 / 5.4.6 | 5.8.0 / 5.8.0 / 5.8.0 |
| AppMetrica Unity / Android | 6.7.x / 7.12.0 | 6.10.0 / 8.5.1 |
| EDM4U | 1.2.187 | 1.2.189 |

Firebase remains compatible with Android API 23; Adjust and the inspected
AppMetrica Android artifacts declare API 21. MAX determines the combined
minimum of 24. Firebase 13.17.0 raises Apple requirements to iOS/tvOS 15 and
Xcode 26.2; iOS builds are outside this Android verification.

## Pinned MAX adapter set

All 17 supported, allowed Android adapters are pinned in `AppLovinPackageInstaller`.
UPM versions are read from the AppLovin registry rather than inferred from
native version numbers. Replacing MAX updates the installed adapters and
preserves their network selection. Automatic initial setup installs the
full allowed set. Forbidden networks remain excluded.

**HyprMX and Maio are removed:** MAX 8.6.6 marks these networks obsolete and
automatically removes their packages. They are excluded from installation and
restoration to avoid reinstalling them every time Unity starts. The remaining
networks keep their settings. This follows the vendor's
[startup migration](https://github.com/AppLovin/AppLovin-MAX-Unity-Plugin/blob/master/DemoApp/Assets/MaxSdk/Scripts/IntegrationManager/Editor/AppLovinInitialize.cs).

| Network | UPM version | Native adapter |
|---|---|---|
| Bigo | 6010000.0.0 | 6.1.0.0 |
| Pangle / ByteDance | 803000401.0.0 | 8.3.0.4.1 |
| Chartboost | 9140101.0.0 | 9.14.1.1 |
| Meta | 6220000.0.0 | 6.22.0.0 |
| InMobi | 11040103.0.0 | 11.4.1.3 |
| ironSource | 906000000.0.0 | 9.6.0.0.0 |
| LINE | 300001010.1.0 | 3000.1.1.1 |
| Mintegral | 17018100.0.0 | 17.1.81.0 |
| MobileFuse | 1120000.0.0 | 1.12.0.0 |
| Moloco | 4120000.0.0 | 4.12.0.0 |
| Ogury | 6030100.0.0 | 6.3.1.0 |
| PubMatic | 5040000.0.0 | 5.4.0.0 |
| Smaato | 23020200.0.0 | 23.2.2.0 |
| Unity Ads | 4200100.0.0 | 4.20.1.0 |
| Verve | 3090200.0.0 | 3.9.2.0 |
| Vungle | 7070801.0.0 | 7.7.8.1 |
| YSO | 1030900.0.0 | 1.3.9.0 |

## Other modules reviewed

- Amazon Appstore SDK 3.0.9 is current. Its existing Unity JNI compatibility
  bridge and native ABI restrictions are preserved.
- Cross-Promo's ExoPlayer 2.19.1 is the final legacy release. Moving it to
  Media3 requires rewriting and validating the custom Java playback bridge;
  changing its Maven version alone would break that integration.
- Embedded UniWebView 6.1.0 is a commercial distribution. A newer licensed
  vendor package is needed for a separate vendor update.
- Local Analytics, Internet Connection and Ingame Debug Console did not
  require native dependency upgrades.

## Sources and validation scope

- [MAX releases](https://github.com/AppLovin/AppLovin-MAX-Unity-Plugin/releases/latest)
- [AppLovin Android 13.6.3 minimum API change](https://github.com/AppLovin/AppLovin-MAX-SDK-Android/releases/tag/release_13_6_3)
- [AGP 8.13.2 compatibility and Kotlin 2.3 support](https://developer.android.com/build/releases/agp-8-13-0-release-notes)
- [Firebase Unity release notes](https://firebase.google.com/support/release-notes/unity)
- [Adjust 5.8.0](https://github.com/adjust/unity_sdk/releases/tag/v5.8.0)
- [AppMetrica Unity](https://github.com/appmetrica/appmetrica-unity-plugin/tree/v6.10.0)
- [AppMetrica Android release notes](https://appmetrica.yandex.com/docs/en/sdk/android/changelog-android)
- [EDM4U 1.2.189](https://github.com/googlesamples/unity-jar-resolver/releases/tag/v1.2.189)

The initial dependency audit inspected 279 resolved AAR manifests. The final
17-adapter set and updated Firebase/AppMetrica successfully build an APK
through Unity 2022.3.60f1 (Mono, armeabi-v7a): minSdk 24, targetSdk 34,
compileSdk 36. Automatic preparation was checked by downloading tools from
scratch and by importing the SDK into a clean consumer with no external SDKs.
All 25 AppMetrica Java bridge files compile against native 8.5.1; enabled
Adjust vendor/module code compiles for both Editor and Android Player.
See `REPORT.md` for scope and local evidence. A successful build does not
replace ad serving, analytics delivery or purchase-flow checks on a device.
