# Changelog

All notable changes to the AMZN GoD SDK package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.8] - 2026-10-05

### Fixed

- Require Firebase App, Analytics, Crashlytics and Remote Config assemblies
  before enabling the Firebase module's compilation define.
- Keep exported Android Gradle wrappers on 8.13 with its pinned SHA-256 and
  apply the AGP profile after MAX's Android postprocessor.
- Handle empty UPM scoped registries without invalid JSON. Resolve AppHud
  templates from physical package paths and remove SDK-owned dependencies
  when the feature, AppMetrica module or SDK is disabled.
- Hide CP and MAX banners behind fullscreen ads and hidden Unity UI; preserve
  the game's CanvasGroup state instead of overwriting it each frame.
- Reserve the analytics queue for regular events by limiting banner impressions
  to 10 of 50 entries. Persist CP clicks before waiting for the device ID and
  run ExoPlayer backend click tracking independently of Adjust requests.
- Revalidate Fire advertising IDs on startup and foreground, reject stale/zero
  cached IDs, and include the current identity in IAP/attribution deduplication.
- Isolate concurrent iOS Adjust getter callbacks, including timeout variants,
  by request ID across the managed/native bridge.
- Preserve IAP retry/watchdog deadlines when the SDK object is deactivated;
  reject expired responses and isolate reentrant Restore completion callbacks.
- Avoid claiming an existing game pause or restoring a stale time scale after
  the game has resumed while offline.
- Fix MAX banners to standard dp sizes (320 x 50 on phones, 728 x 90 on
  tablets), disabling adaptive height and screen-wide stretching. Expose
  `StandardBannerSizeDp` and document density/render-aware UI reservation;
  retain Cross-Promo's 396 x 80 size and scaling.
- Allow Android-only MAX replacement when legacy adapters declare iOS CocoaPods
  or leave iOS native files. Update only Android UPM adapters, preserving
  settings and the existing backup/restore flow.
- Use `placement: "banner"` for AppMetrica cross-promo banner clicks, matching
  the backend. Keep the `crosspromo_banner_click` event name unchanged.
- Track MAX banner clicks, impressions and revenue across analytics channels;
  use revenue callbacks for impressions and retain late callbacks after hide.
- Keep analytics `placement` canonical (`interstitial`, `rewarded`, `banner`)
  across cross-promo, MAX custom/backend events and native revenue reports.
  Custom MAX placement settings apply only to MAX shows. Preserve original
  tracking signatures and unknown-placement payload behavior.
- Send cross-promo banner impressions and clicks to the backend with
  `placement: "banner"`. Start backend click tracking before redirect, with
  up to 1.5 seconds of waiting and tracking hosted on the persistent module.
- Verify every split APK from the current Android build report; distinguish
  Gradle export from a directory containing completed APKs.
- Preserve allowed Gradle dependencies with prohibited transitive exclusions,
  Maven group identity in map notation and multiline comment boundaries.
- Restrict prohibited MAX package cleanup to Android and preserve iOS packages,
  CocoaPods declarations and shared legacy files during Android cleanup.

### Changed

- Read Fire advertising ID directly from Fire OS settings after reading the
  advertising opt-out preference; use Adjust only if system access fails.
  ID use remains limited to conversion tracking and reporting.
- Cache SDK Settings dependency status and refresh it after relevant changes,
  keeping repeated package and file checks out of window rendering while
  preserving unsaved settings and dependency retry controls.
- Automatically remove prohibited Android SDK dependencies and known carriers
  (ironSource, Unity Ads, AppMetrica Fyber revenue bridge and MAX Quality Service).
  Verify final APK/AAB DEX definitions and references after every Android build;
  fail the build if prohibited types remain. Applies even with AppLovin disabled.
  See `Documentation~/AMAZON-SDK-CLEANUP.md`.
- Run synchronous cleanup before every Android build: sanitize custom Gradle
  templates and exclude recognized native plugins, then continue the same
  build without a UPM cleanup/retry cycle. Preserve source libraries and
  back up changed templates; defer package synchronization until after builds.

- Retain Unity 2022.3 and update Android dependencies for API 24: MAX Unity
  8.6.6 / Android 13.6.4 with 16 allowed adapters pinned, Firebase 13.17.0,
  Adjust 5.8.0, AppMetrica Unity 6.10.0 / Android 8.5.1 and EDM4U 1.2.189.
- Automatically prepare dependencies of enabled modules and the Android build
  tools. Use AGP 8.13.2, Gradle 8.13, a separate JDK 17 and compileSdk 36 while
  preserving Unity's bundled JDK/SDK/NDK and the project's target API.
- Raise the Android minimum to API 24 when MAX is enabled and remove the old
  generated R8/Kotlin overrides. See `Documentation~/ANDROID-DEPENDENCIES.md`.
- Stop installing HyprMX and Maio, which MAX 8.6.6 removes as obsolete networks.

### Added

- Import the Unity IAP product catalog from SDK Settings without requiring
  Unity Purchasing. Prefer AmazonApps store IDs, preserve existing products
  and skip duplicate SKUs; set subscription terms and save the draft to apply.
- Switch the Cross-Promo banner to a bottom-center MAX banner after JSON
  interstitial/rewarded caps are exhausted. Keep CP when MAX is not ready;
  expose a default-off debug override for testing cap exhaustion.
- Install Google AdMob with the required MAX Android adapters and configure
  its Android App ID in SDK Settings. Validate the effective ID before builds;
  an empty SDK field preserves an existing MAX Integration Manager value.
- Configure the built-in Adjust startup Remote Config key in SDK Settings → Firebase.
  Existing configs retain `adjust_enable`; each custom key keeps its own offline decision cache.
- Configure AppLovin interstitial and rewarded AD Placements in SDK Settings.
  Pass them to MAX shows; SDK analytics and revenue reports retain canonical
  placement values. Empty or missing settings retain
  the existing `interstitial` and `rewarded` defaults.

### Upgrade notes

- Install the stable channel from `#Releases` or pin `#v1.0.8`. This release
  includes the changes shipped in `v1.0.8-beta.1` and `v1.0.8-beta.2`.
- Unity 2022.3 remains supported. The enabled MAX stack requires Android API
  24, 16 adapters plus MAX core, and a valid AdMob Android App ID. See
  `Documentation~/ANDROID-DEPENDENCIES.md` before updating a consumer project.
- Legacy Unity Video Player was excluded from the final review fixes. Its
  known reward-callback issue remains; the detailed scope and limitations are
  in `Documentation~/Reviews/2026-10-05/FIXES.md`.
- Validation: 30 isolated C# compiler invocations passed with Unity
  2022.3.60f1, including module-toggle variants and the managed iOS Adjust
  bridge. Full Android/iOS builds, Unity runtime and device checks were not
  rerun for this release; the iOS native bridge still needs Xcode validation.

## [1.0.7] - 2026-09-22

### Fixed

- Synchronize the AppLovin SDK key from SDK Settings to MAX settings when
  saving, after Editor reload and before Android or iOS builds. This prevents
  Quality Service from failing Android builds with an empty SDK key when the
  key is already configured in SDK Settings. Preserve the MAX key when the
  SDK Settings key is empty; keep MAX optional.

### Changed

- Enable Firebase Remote Config by default for new SDK settings so the built-in
  `adjust_enable` startup flag works without adding constants through the
  Firebase module UI. Preserve explicitly saved Remote Config preferences.
- Replace the manual Adjust flag constants button with built-in flag guidance
  and document the required Firebase Console string value and startup behavior.

## [1.0.6] - 2026-09-22

### Fixed

- Automatically replace previously saved Cross-Promo Config URLs with
  `https://amzngod.space/master.json` once on SDK upgrade, including in already
  open SDK Settings windows. Persist the migration without requiring Save
  Settings; keep subsequent manual URL changes after migration.
- Fall back to JSON Cross-Promo creatives beyond their display caps when the
  normal JSON pool is exhausted and AppLovin MAX is not ready to accept the
  requested interstitial or rewarded ad. Preserve counters, creative rotation,
  cooldowns, filtering and existing reward callbacks; ready MAX ads retain
  priority after the normal JSON pool is exhausted.
- Preserve the full creative pool and preload the next JSON fallback after
  display caps are reached. Prevent fallback from opening over an active MAX
  ad or after an asynchronous failure of an already accepted MAX show.

## [1.0.5] - 2026-09-21

### Added

- Firebase Remote Config A/B testing with string groups, local control defaults,
  group callbacks, runtime group lookup and exposure events.
- Configure tests, groups and the default group in SDK Settings; generate C#
  constants when saving and register the configured tests at SDK startup.
- Built-in `adjust_enable` string flag (`true` / `false`) checked before Adjust
  initialization. The decision is fixed for the application session, with a
  bounded startup wait and cached or local defaults when fetching fails.

### Changed

- Show `https://amzngod.space/master.json` as the editable Cross-Promo default
  when settings are new or empty; preserve existing custom URLs.

### Fixed

- Skip Adjust events, attribution and ad revenue until Adjust is initialized,
  including when its startup is disabled through Remote Config.
- Remove the obsolete `MaxSdk.Scripts` assembly reference from the AppLovin module.

## [1.0.4] - 2026-09-08

### Fixed

- Restore SDK Editor compilation with Cross-Promo enabled by explicitly
  referencing the Core assembly used by the Cross-Promo cap debug tools.

## [1.0.3] - 2026-09-08

### Added

- Select per-game Cross-Promo creative configs through a shared master JSON,
  while retaining support for direct creative config URLs.
- Support fixed creative positions in Cross-Promo video rotation.

### Fixed

- Ignore old Gradle build outputs and caches when checking disabled SDK modules.
- Preserve shared Android libraries and vendor manifest components, including
  AppLovin required by Appodeal; limit cleanup to explicitly SDK-owned code.
  Dependency resolution remains under the consuming project's control.
- Ignore Cross-Promo creative caps when the AppLovin module is disabled.
- Detect locked or read-only Firebase files before downloading or replacing the
  SDK, and explain when a full Unity Editor restart is required.
- Roll back only attempted Firebase file changes and skip files already matching
  the backup, avoiding false incomplete-rollback errors for unchanged native DLLs.

## [1.0.2] - 2026-09-07

### Changed

- Move the advertising banner into the optional CrossPromoBanner sample. The
  SDKPrefab initializes the Cross-Promo manager without creating a banner.

### Fixed

- Remove the legacy Cross-Promo banner template that shared a GUID with
  existing project banners; give the remaining module templates unique GUIDs.

## [1.0.1] - 2026-09-07

### Added

- Install Firebase Unity 12.8.0 (Analytics, Remote Config and Crashlytics)
  directly from SDK Settings, with backups and rollback on installation errors.
- Separate replacement buttons for Firebase and AppLovin MAX 8.6.3; replacement
  is disabled when the required SDK version is already installed.
- AppLovin mediation module, ad routing and analytics, Android build guards,
  and installation of allowed Android adapters with required version pins.
- Per-creative cross-promo display limits and an editor counter reset tool.

### Fixed

- Exclude iOS-only CSJ, Pangle and Tencent GDT packages from Android adapter
  installation. Install adapters in one UPM operation and roll back only
  dependencies actually added to the project manifest.
- Preserve AppLovin packages, versions and settings while its module is disabled;
  serialize module synchronization with SDK installation and replacement.
- Preserve disabled Firebase dependency files during SDK replacement, include
  Remote Config in dependency switching, and exclude external Firebase native
  plugins from builds when the module is disabled.
- Keep disabled modules out of builds, including their resources, native
  plugins, managed assemblies and dependencies; support prefab import while
  modules are disabled.
- Correct installer asset GUIDs and release extraction on long Windows paths.

## [1.0.0] - 2026-08-23

First release distributed through Unity Package Manager.

### Added

- UPM package distribution: install via git URL from the `Releases` branch
  (`https://github.com/Amazon-GoD/AMZNGoDSDK.git#Releases`) or pin a version
  with `#vX.Y.Z`.
- Per-module assembly definitions (`AMZNGoDSDK.Module.*`) gated by
  `AMZN_<MODULE>_ENABLED` define constraints; the core facade
  (`AMZNGoDSDK.Core`) compiles against whichever modules are enabled.
- `NativePluginRegistry` + `NativePluginBuildFilter`: native plugins
  (.jar/.aar/.so/.mm/.java) of disabled modules are excluded from builds via
  `PluginImporter.SetIncludeInBuildDelegate` — works inside immutable UPM
  packages without touching `.meta` files.
- `EdmDependencyGenerator`: EDM4U dependency templates of enabled modules are
  merged into a single generated
  `Assets/AMZNGoDSDKGenerated/Editor/AmznGoDSdkDependencies.xml` in the
  consumer project; regenerated automatically whenever module toggles change
  and before every build.
- Firebase module is a regular visible module with its own asmdef
  (`AMZNGoDSDK.Module.Firebase`).

### Changed

- Module toggles are now defines-only (SDK Settings window writes
  `AMZN_*_ENABLED` scripting defines). The legacy folder-rename toggle
  mechanism (`Module~` hiding) has been removed — it cannot work inside an
  immutable UPM package.
- Minimum supported Unity version raised to 2022.3.
- Dependencies audit: the package depends on `com.unity.ugui`,
  `com.unity.textmeshpro` and the required built-in engine modules
  (`androidjni`, `imageconversion`, `unitywebrequest`,
  `unitywebrequesttexture`, `video`). `com.unity.purchasing` and
  `com.unity.services.core` were removed — the SDK does not reference them
  (in-app purchases use the vendored Amazon Appstore IAP SDK).

### Fixed

- AppMetrica Android bridge build guard now finds the bridge `.java` sources
  when the SDK is installed as a UPM package (it scanned only `Assets/`
  before and failed every Android build with AppMetrica enabled).
- Adjust manifest preprocessor no longer throws `DirectoryNotFoundException`
  into the build log of consumer projects that have no
  `Assets/Plugins/Android/AndroidManifest.xml`.
- Orphan `.meta` files of empty folders no longer ship in the release tree
  (removed the import warnings `A meta data file exists but its folder can't
  be found`).

### Removed

- `DependencyPreprocessor` (moved XML files inside the SDK folder during
  builds — incompatible with immutable packages), replaced by the EDM
  template generator.
- `ModuleFolderManager`, `ModuleFolderWindow`, `ModuleFilesWrapper`,
  `AutoWrapAllFiles` (folder-rename toggle machinery).
