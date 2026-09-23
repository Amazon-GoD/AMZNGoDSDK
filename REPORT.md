# Android SDK upgrade — 2026-09-23

The update retains Unity 2022.3.60f1 and Android **minSdk 24**. MAX Unity
8.6.6 / Android 13.6.4, Firebase Unity 13.17.0, Adjust 5.8.0, AppMetrica Unity
6.10.0 / Android 8.5.1 and EDM4U 1.2.189 are integrated. The supported MAX
adapter set contains 17 pinned networks; HyprMX and Maio are obsolete in MAX
8.6.6 and are no longer installed or restored.

Enabling modules and saving settings starts automatic dependency preparation.
The installer survives ordinary Unity script reloads, verifies completed
installations, and provides status and retry after a real error. Android tools
are discovered locally or downloaded with published checksum verification.
No manual JDK, Gradle or SDK path configuration is needed for normal setup.

Build profile: AGP 8.13.2, Gradle 8.13, JDK 17, compileSdk 36 and Build Tools
36.0.0. Unity keeps its bundled JDK/SDK/NDK; generated Gradle files explicitly
use Unity's NDK version. The existing target API remains 34. The SDK raises
the minimum automatically when necessary for enabled modules.

## Verification

- Full Unity 2022.3 C# compilation: passed.
- Full Android APK through Unity BuildPipeline: passed, zero build errors.
- APK manifest: minSdk 24, targetSdk 34, compileSdk 36.
- Starting a build at minSdk 23: production preflight raises it to 24 and the
  resulting APK builds successfully.
- Fresh automatic tool installation: passed with no preinstalled modern
  tools. Downloaded Adoptium JDK 17.0.20.1, Gradle 8.13, Android Platform 36
  revision 2 and Build Tools 36.0.0; checksums and installed files verified.
- First import into an isolated Unity 2022.3 consumer with no EDM, MAX or
  Firebase: automatic setup passed with all 17 supported adapters. Restart
  also passed without repeated installation or obsolete packages.
- AppMetrica: all 25 Java bridge sources compile against native SDK 8.5.1.
- Adjust: vendor SDK and SDK module compile with Adjust enabled for both
  Unity Editor and Android Player.

The full APK uses **Mono / armeabi-v7a**, matching this project's selected
backend and architecture. IL2CPP, arm64, iOS and runtime behavior on an
Android device were not verified by these builds. The Unity Test Framework
was not run, as disabled in project instructions.

Detailed package versions, sources and unchanged modules are documented in
[Android dependencies](Documentation~/ANDROID-DEPENDENCIES.md). Local logs,
download verification and the APK are under
`Temp~/api24-upgrade-validation` (not included in the SDK distribution).
Final build evidence: `unity-final-guards-build.log`, `unity-build-result.txt`
and `AMZNGoDSDK-api24.apk`. Fresh import and restart evidence:
`CLEAN-CONSUMER-IMPORT.md` and `CleanConsumer-3/clean-consumer-restart-result.txt`.

## Other modules

Amazon Appstore SDK 3.0.9 is already current. ExoPlayer remains at the final
legacy 2.19.1 release because Media3 needs a separate playback-bridge migration.
UniWebView remains at the embedded licensed 6.1.0 distribution. Local SDK
modules did not need external dependency changes. Existing module toggles,
network exclusions and safe-branch restrictions are preserved.
