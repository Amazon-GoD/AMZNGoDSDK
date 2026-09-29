# Cross-promo event placements — 2026-09-29

Implemented on `tmp/crosspromo-event-placements`, based on `safety`, in a
separate worktree. No merge or push was performed.

Existing cross-promo impressions and clicks include `placement`:
`interstitial`, `rewarded`, or `banner`. Both video players pass the format;
native asynchronous clicks retain a snapshot of the placement at click time.
The retry queue preserves placement in the stored event JSON. Legacy API
overloads remain available and omit the field when the placement is unknown.

Banner impressions are handled in another branch; their future call is
`TrackImpression(paidAppId, "banner")`. Install attribution requires the backend
to inherit placement from the attributed `cp_click`, matching `device_id_hash`
and the click's `paid_app_id` to the installed game's `app_id`. Backend code
is absent here; first-open events and Adjust URLs are unchanged.

Validation: production C# compiled using Unity 2022.3.60f1 compiler/references
and freshly built intermodule references for `EditorAndroid-Enabled`,
`EditorAndroid-Disabled`, `AndroidPlayer-Enabled`, and `AndroidPlayer-Disabled`.
Runtime, Analytics (when enabled), CrossPromo, and Core passed. Only existing
CS0414 warnings for `_lastRequestTime` and `_firstWarmupTriggered` remained.
Logs: `Temp~/PlacementValidation/<variant>/*.log` in the worktree.

Independent read-only architecture review: **APPROVE**, `findings=[]`.
`git diff --check`: **PASS**. The test agent and Unity Test Framework remain
disabled. Live HTTP delivery, backend processing, and install attribution
were not exercised.

# Automatic prohibited SDK cleanup — 2026-09-29

## Follow-up: five review fixes

- Split APK builds verify every APK/AAB listed in the current BuildReport.
  Export mode is captured before building; a directory alone no longer skips
  verification. Stale files in that directory are not selected.
- Gradle dependency identity is parsed before its configuration closure.
  Explicit exclusions are preserved, Maven maps retain group/name identity,
  and statement removal preserves complete comment tokens and line endings.
- Prohibited MAX package removal is scoped to Android. Legacy cleanup edits
  only prohibited Android XML nodes, preserving iOS packages, CocoaPods,
  shared files and metadata, including export-labelled legacy installations.

Validation: 23 artifact-selection probes, 32 Gradle transformation probes and
24 iOS-preservation/rollback assertions passed against the production code.
Source-template byte-exact backups and repeated cleanup passed; the cleaned
template parsed successfully with Groovy 3.0.10. Editor code compiled with
and without Android symbols against Unity 2022.3.60f1 (only the two existing
unused-variable warnings). Unity Test Framework remains disabled; a full
split-APK build and live iOS UPM operations were not run for this follow-up.

Diagnostics are under ignored `Temp~/AmazonSdkCleanup/ArtifactGuardProbe`,
`Temp~/GradleReviewProbes.cs`, `Temp~/GradleReviewInputs-*` and
`Temp~/IosPreservationCheck`. Changes are intended for a local commit only.

## Follow-up: clean and continue in Build PreProcess

Every Android build with the SDK enabled now synchronously cleans existing
main/launcher/base Gradle templates, reapplies Quality Service settings and
excludes recognized prohibited native plugins through Unity's importer build
delegate. Original libraries and immutable UPM contents remain untouched;
changed templates have byte-for-byte backups under Library.

The same build continues. Installed prohibited registry MAX adapters and a
queued background cleanup are no longer reasons to abort/retry a ready build.
Actual package operations and missing required dependencies retain their
normal readiness checks. Deferred MAX package synchronization does not start
during BuildPipeline. Unknown nonregistry wrappers retain diagnostics.

EDM was observed rewriting source templates during OnPostProcessScene, after
PreProcess. Therefore the late generated-project callback reapplies source
cleanup too, before cleaning the exported project.

Verification of the final change:

- Full Unity 2022.3.60f1 BuildPipeline Android build in the isolated consumer:
  **passed in one invocation**, with ironSource and Unity Ads UPM adapters
  deliberately installed and background package cleanup suspended.
- Injected forbidden Gradle dependencies were removed. An injected real JAR
  containing `com.tapjoy.PrebuildProbe` remained in source Assets but was
  excluded from the build. Final APK: all seven SDKs `DEF=0, REF=0`.
- Source template cleanup, byte-exact backups, UTF-8 BOM/CRLF preservation,
  retained allowed dependency, missing optional templates and idempotence:
  passed using the production transformation.
- C# compilation with and without Android symbols passed; final read-only
  review approved. Unity Test Framework remains disabled.

Evidence: `Temp~/AmazonSdkCleanup/unity-preprocess-build-final.log`,
`Consumer/preprocess-build-result.txt`, `Consumer/preprocess-probe.apk`,
and `template-cleanup.txt`. The earlier probe exposed EDM's source rewrite
after a successful APK build; the final run verifies that correction too.

## Original implementation and verification

Implemented on `tmp/amazon-prohibited-sdk-cleanup`, based on `safety`.
Android builds with the SDK enabled now remove known prohibited dependencies
and carriers even when the AppLovin module is disabled. The installer removes
recognized MAX packages with backup/rollback and cannot reinstall them from
pins or disabled state. The allowed Android adapter set is now 15 networks.

ironSource and Unity Ads are removed as whole carriers. AppMetrica retains its
core and excludes only the optional Fyber ad-revenue bridge. Android MAX
Quality Service is disabled through Unity's live settings API, independently
of SDK key configuration. Late Gradle cleanup removes stale instrumentation,
recognized native dependencies and copied libraries; exclusions cover the
root project, launcher and unityLibrary and are refreshed on every run.

Final APK/AAB verification checks DEX definitions and references, including
multidex, AAB module entries, extensionless DEX and nested ZIP contents. Type
findings or unreadable/unsupported DEX fail verification. Strings are reported
separately. Export-only Gradle builds explicitly require a later artifact scan.

## Verification of this change

- Editor C# compiled against Unity 2022.3.60f1 references with and without
  `UNITY_ANDROID`. Only two pre-existing unused-variable warnings remain.
- Isolated Unity consumer upgraded from the prior 17-adapter setup:
  automatic cleanup passed, 15 allowed adapters remained, Quality Service
  was off, and dependency bootstrap completed.
- Restart of that consumer passed. The production verifier rejected the
  original APK and accepted the cleaned APK inside Unity.
- Actual production Gradle cleanup ran on a copy of the prior generated
  Android project. Repeating it made no further changes.
- Offline Gradle 8.13 `assembleRelease` succeeded (133 tasks). Result: Mono,
  armeabi-v7a APK, using the prior Unity-exported game content.
- Original APK: Tapjoy `DEF=0, REF=20`; Inneractive/Fyber `DEF=0, REF=58`.
- Rebuilt APK: **all seven SDKs `DEF=0, REF=0`** across eight DEX files.
  Remaining string counts: Tapjoy 2, MoPub 2, Inneractive/Fyber 6.
- Nine standalone scanner smoke checks passed: definitions for all seven,
  AAB array references, string-only content, extensionless/nested DEX,
  malformed offsets/magic, missing DEX and unsupported version.
- Read-only implementation review and `git diff --check` passed.

Unity Test Framework was not run, as disabled by project instructions. The
APK was rebuilt from a copied Gradle export; a new full game export from
Unity, real-device ad serving, and an actual AAB build were not performed.
Embedded/local UPM packages, dependencies retained by other packages and
unknown legacy code receive actionable diagnostics rather than blind deletion.

Evidence is under `Temp~/AmazonSdkCleanup` (excluded from distribution):
`before-dex.txt`, `after-dex.txt`, `gradle-build.log`, `gradle-cleanup.txt`,
`consumer-first-pass.txt`, `Consumer/cleanup-result.txt`, and both Unity logs.
Instructions: [Automatic cleanup](Documentation~/AMAZON-SDK-CLEANUP.md).
Existing user changes to the AppLovin asmdef and crosspromo config were
preserved. No merge or push was performed.

---

# Previous validation: Android SDK upgrade — 2026-09-23

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
