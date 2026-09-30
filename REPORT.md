# Advertising placement consistency — 2026-09-30

Implemented on `tmp/placement-event-consistency`, created from local `safety`.

- Cross-promo banner clicks now use `placement="banner"` in AppMetrica and
  backend events. The event name is unchanged.
- Added the shared `AdAnalyticsFormat` constants and optional `ad_format` to
  advertising custom events, backend CP/mediation payloads and native MAX
  revenue metadata. Configured MAX placement names retain their values.
- Existing public tracking signatures remain; new overloads accept explicit
  format independently of placement. Legacy CP calls infer only exact known
  formats. Legacy mediation calls omit the format. Unknown formats are omitted,
  and existing unknown-placement serialization rules are preserved.
- Added MAX banner click, load-error and revenue callbacks with matching
  subscribe/unsubscribe and ad-unit filtering. `SetBannerPlacement` sets
  `banner` immediately after creation. Revenue callbacks produce banner
  displayed events and native/backend revenue, including refreshes and late
  revenue after hide. Loading/showing/hiding does not synthesize impressions.
- Updated the SDK documentation and changelog with the payload contract and
  backend compatibility requirement.

Two independent static reviews approved the AppLovin implementation and the
shared/backend/CrossPromo implementation without blockers. Unity 2022.3.60f1
Roslyn compilation passed **44/44 module builds** across eight configurations:
Editor Android / Android Player × Analytics on/off × AppLovin on/off. The
shared Runtime assembly and dependent modules were rebuilt with fresh
references. Remaining compiler diagnostics were existing CS0168/CS0414 and
the obsolete `MaxSdk.SetSdkKey` CS0618 warning.

Evidence is in ignored `Temp~/PlacementConsistencyValidation/Summary.log`,
response files, compiler logs and source hashes. Original line endings were
restored on unchanged lines after compilation; normalized source content was
checked to remain identical. The only later source adjustment moved an XML
documentation comment to its intended method. Whitespace checks passed.
Unity tests are disabled by AGENTS.md and were not run. No device/Editor play
or live analytics-delivery validation was performed.

The backend implementation is outside this repository. It must accept optional
`ad_format` on both CP and mediation events and continue accepting older queued
payloads without it; server acceptance has not been verified here. Existing
first-open attribution behavior and Adjust tracking URLs are unchanged.

The completed tmp branch is intended for a local fast-forward merge into
`safety`. No push or changes to `main` are part of this task.

---

# Integration of agent tmp branches — 2026-09-29

Audited all 29 existing local `tmp/*` branches against `safety` at `8423620`.
26 were already ancestors of `safety`; the three remaining changes were merged
on `tmp/integrate-agent-changes`, created from `safety`:

| Source branch | Source commit | Integration merge |
| --- | --- | --- |
| `tmp/crosspromo-banner-backend` | `378cf42` | `1883fc4` |
| `tmp/crosspromo-event-placements` | `3961c45` | `238dc39` |
| `tmp/crosspromo-banner-caps` | `099a3e4` | `a852d77` |

A read-only remote check found no remote `tmp/*` branches and confirmed
`origin/safety` at `d0904c3`. All 30 local tmp refs, including the integration
branch, are covered by the validated integration history, ready for a
fast-forward merge into `safety` after this report is committed.

Overlapping placement overloads were combined consistently. Banner clicks
retain exactly one backend event, persistent-module tracking and the bounded
1.5-second wait before redirect. Banner cap switching and MAX lifecycle were
preserved. A visibility guard prevents fully transparent CanvasGroups from
generating banner impressions. Contradictory README statements were corrected;
all earlier report sections below were retained as historical task records.
No conflict required an ambiguous product decision.

Independent final reviews of events and banner/cap integration approved
`a852d77` with no remaining findings. Whitespace and conflict-marker checks
passed, and no Infatica code was added.

Unity 2022.3.60f1 Roslyn compilation passed **44/44 module builds** across eight
configurations: Editor Android / Android Player × Analytics on/off × AppLovin
on/off. Runtime, Firebase, enabled optional modules, CrossPromo, Core and Editor
were rebuilt with fresh references. All 138 compiled source files stayed
unchanged during validation; disabled module defines/references were excluded.
Only CS0168, CS0414 and the existing obsolete `MaxSdk.SetSdkKey` warning remained.
Evidence: ignored `Temp~/AgentIntegrationValidation/Summary.log` and adjacent
response files/logs. Unity tests remain disabled by AGENTS.md; no Editor play,
device or live backend validation was performed.

No push, changes to `main`, branch deletion or worktree cleanup was performed.

---

# Dev Crew report

**Date:** 2026-09-29
**Task:** Switch the cross-promo banner to AppLovin MAX when all JSON interstitial/rewarded caps are exhausted.

## What was done

- ✅ Reused the existing `HasFill` cap decision; an empty or unloaded JSON config does not trigger banner switching.
- ✅ Added bottom-center MAX banners and `BannerAdUnitId` settings; cross-promo remains when the ID is empty or MAX is not ready.
- ✅ Added 0.25-second cap checks and handling for no-ads, explicit hide, fullscreen ads, disable/destroy and scene changes; analytics and cap counters are unchanged.
- ✅ Worked in isolated `tmp/crosspromo-banner-caps`, created from `safety` at `4526aa3`; no merge or push.

## Architecture

`CrossPromoModule` exposes the existing cap decision to `CrossPromoBanner`, which selects cross-promo or MAX and controls visibility.
`AppLovinModule` owns the native banner lifecycle and pauses refresh while hidden.
Editor settings pass the new banner ad unit through the runtime configuration and SDK core; MAX calls remain behind `AMZN_APPLOVIN_ENABLED`.

## Files created/modified

- `Editor/SdkModulesSettings/ModulesSettings/AppLovinSettingData.cs` — editor banner ad unit field.
- `Editor/SdkModulesSettings/SdkSettingsManager.cs` — banner setting conversion in both directions.
- `Editor/Windows/SDKSettingsWindow.cs` — Banner Ad Unit input and help text.
- `Runtime/Core/AmznGoDSDKCore.cs` — passes the banner setting to AppLovin.
- `Runtime/DataLoader/ModulesSettings/AppLovinSettingData.cs` — runtime banner ad unit field.
- `Runtime/Modules/AppLovin/AppLovinModule.cs` — native banner creation, show/hide, refresh and cleanup.
- `Runtime/Modules/Cross-Promo/CrossPromoModule.cs` — shared cap decision for banners.
- `Runtime/Modules/Cross-Promo/Pyro Entertainment/Video Cross Promo Plugin/Banner/CrossPromoBanner.cs` — switching, visibility and lifecycle handling.

## Review results

Approved after 1 review iteration; final issue counts: critical 0, high 0, medium 0, low 0.
`git -c core.whitespace=cr-at-eol diff --check` passed.

## Tests

Testing disabled — skipped (Unity Test Framework/tester, per AGENTS.md).
Unity 2022.3.60f1 Roslyn compilation using Bee response files and isolated worktree sources passed: Editor with MAX 5 assemblies, without MAX 4; Android Player with MAX 4, without MAX 3. All exited 0.
Only existing warnings remained: obsolete `SetSdkKey`, unused `_firstWarmupTriggered`, and editor variables `we`/`e`.
Compilation evidence is under ignored `Temp~/BannerCaps`; no device/MAX rendering test was run.

## Known limitations

A live Banner Ad Unit ID has not been configured; it must be supplied before MAX banners can appear.
`ShowBanner()` returning true means MAX accepted the request; loading or no-fill can leave a gap after cross-promo is hidden.
Device behavior, including native layout, scene transitions and lifecycle scenarios, still needs manual validation.

## How to use

Enable AppLovin and set `AMZN GoD > SDK Settings > AppLovin > Banner Ad Unit`, then save settings.
On a device, exhaust the last JSON cap through an interstitial and through a rewarded ad; confirm MAX appears after fullscreen closes, and also on restart with persisted caps.
Check no-ads, `hide()`/`UpdateBannerUI()`, disable/enable and scene changes; confirm empty JSON, missing ID or unavailable MAX retain the cross-promo path.

---

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

# Cross-promo banner backend events — 2026-09-29

Implemented on `tmp/crosspromo-banner-backend`, created from `safety` at
`4526aa3`. Work is isolated in `Temp~/CrossPromoBannerBackend` because other
tasks changed the shared checkout's branch during investigation.

- Visible banner rotation sends `cp_impression` with `placement: "banner"`.
  Hidden banners, disabled Images, missing sprites and no-ads are excluded.
- Banner clicks send exactly one `cp_click` with the same placement. Backend
  tracking starts independently of Adjust/TrackingUrl. Redirect waits at most
  1.5 seconds, and pending tracking runs on the persistent module.
- Both modules retain their existing public single-argument methods and add
  placement overloads. The JSON queue retains placement during retries.
  Existing interstitial/rewarded calls retain their previous payloads.
- `first_open` and incoming attribution are unchanged: the installed game
  does not know its source placement. Backend attribution must inherit it
  from the matched cross-promo event. Server implementation is not in this
  repository; no install-placement support is claimed for the backend.

Validation: independent read-only transport and banner reviews approved.
Analytics and CrossPromo compiled with Unity 2022.3.60f1's Roslyn compiler
against the project's Unity assembly references. CrossPromo compiled with
Analytics enabled and disabled. Only existing unused-field warnings remained
(`DeviceIdProvider._lastRequestTime`, `CrossPromoModule._firstWarmupTriggered`).
Diff whitespace checks used `cr-at-eol` for the repository's stored CRLF C#
files. Compiler responses and logs are under ignored `Temp~/Validation`.

Unity Test Framework remains disabled. No Editor play session, device run,
or live backend delivery was performed. Delivery/enqueue is not guaranteed
within the redirect deadline, especially while device ID is resolving.
No merge or push was performed.

---

# Configurable Adjust Remote Config key — 2026-09-29

Added **SDK Settings → Firebase → Adjust Remote Config Key**, persisted through
editor/runtime settings and JSON. The built-in startup flag uses the selected
key for registration, reads and logs. Blank or missing settings retain
`adjust_enable`; the editor validates 1–100 ASCII identifier characters, and
runtime safely defaults invalid hand-edited JSON. Decisions are cached per
key as `amzn_sdk.<key>`, preserving the previous default cache.

Setup documentation and the changelog describe matching the parameter name
in Firebase Console, retaining string groups `true` / `false`, and restarting
the application after publishing a change.

Validation: independent review approved without findings; `git diff --check`
passed. Unity 2022.3.60f1 Roslyn compilation passed for Runtime, Firebase,
Core and Editor with Firebase enabled, and Runtime, Core and Editor with
Firebase disabled. Only the existing CS0168 warnings in YandexDiskUploader
and SdkSettingsManager remain. Evidence: ignored `Temp~/AdjustEnableKey`.

Unity Test Framework remains disabled. Live Editor UI and Remote Config
fetch on a device were not checked. No push was performed.

---

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
