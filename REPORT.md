# Dev Crew report

**Date:** 2026-10-05
**Task:** Исправить все замечания ревью изменений после v1.0.7, кроме legacy Unity Video Player.

## What was done

- ✅ Подготовлены исправления 14 пунктов R01–R06, E01–E04, E06, D01, C01 и C02; E05 исключён по указанию пользователя.
- ✅ Android wrapper/порядок callbacks, UPM manifest и AppHud dependencies приведены к согласованному поведению.
- ✅ Исправлены видимость баннеров, приоритет очереди, сохранение кликов, Fire ID и iOS Adjust callbacks.
- ✅ Устранены зависание IAP после деактивации, ошибки повторного Restore и перезапись игровой паузы.
- ✅ Три независимых направления ревью одобрили код; финальная изолированная компиляция Run3 прошла 30/30.
- ⚠️ Device/Editor/Gradle/Xcode-приёмка не выполнялась; сценарии и доказательства собраны в [FIXES.md](Documentation~/Reviews/2026-10-05/FIXES.md).

## Architecture

Editor-слой окончательно применяет Android-профиль и синхронизирует optional AppHud XML независимо от define модуля.
CP использует единую проверку видимости; Analytics сохраняет клики перед ожиданием ID и ограничивает частые баннерные показы.
iOS bridge маршрутизирует ответы Adjust по request ID, IAP хранит абсолютные deadlines и разделяет callbacks прогонов.
InternetConnection восстанавливает timeScale только при сохранении собственной паузы; публичные API потребителя сохранены.

## Files created/modified

- `Editor/SdkModulesSettings/{AndroidGradleToolchain,AndroidToolchainSettings,AndroidToolchainInstaller}.cs` — Android profile/wrapper.
- `Editor/SdkDependencies/{AppLovinPackageInstaller,ManifestJson}.cs` — manifest; `Editor/ConditionalCompilation/{AppMetricaAppHudDependencies,EdmDependencyGenerator,DisabledModuleBuildGuard}.cs` и `AppMetricaResolver.cs` — AppHud, с meta новых helpers.
- `Runtime/Modules/Analytics/{AnalyticsEventQueue,AnalyticsModule,DeviceIdProvider}.cs` — очередь, identity и lifecycle.
- `CrossPromoBanner.cs`, `CrossPromoExoNativeOverlay.cs` — баннеры/клики; `AdjustiOS.cs`, `AdjustUnity.h`, `AdjustUnity.mm` — согласованный iOS bridge.
- `IapRetryScheduler.cs`, `InAppPurchaseModule.cs`, `InternetConnectionModule.cs` — восстановление операций и пауза.
- `CHANGELOG.md`, документация SDK/Adjust, `Documentation~/Reviews/2026-10-05/FIXES.md`, `REPORT.md` — миграция, ограничения и результат; полный список путей есть в FIXES.

## Review results

Итог: согласованные исправления одобрены без оставшихся новых замечаний в проверенной области; закрыты P1 — 1, P2 — 10, P3 — 1 и два условных пункта C01/C02.
E05 P2 остаётся исключённым; `CrossPromoVideoOverlay.cs` и `CrossPromoModule.cs` не изменены.
Три независимых направления ревью и повторные проверки после доработок; точное число внутренних итераций ревью не протоколировалось. Проведены три прохода компиляции, финальный — Run3.
Работа подготовлена в `tmp/review-fixes-v107` от `safety` на `ac39f9c`; push не выполнялся, `main` не использовалась.

## Tests

Testing disabled — skipped: AGENTS.md отключает тестер; Unity Test Framework не запускался.
Финальная Unity 2022.3.60f1 Roslyn-компиляция Run3: 30/30 PASS, 0 errors; все 330 source/asmdef SHA-256 совпали до/после.
Проверены Editor Android, Android Player, iOS Adjust C# и пять вариантов module defines; native ABI сопоставлен статически.
Это изолированная компиляция, не сборка Unity/iOS/Android; предупреждения и ограничения сохранены в [локальном COMPILATION.md](Temp~/ReviewFixes20261005/COMPILATION.md).

## Known limitations

Unity Editor, Android export/Gradle, device runtime, Objective-C/Xcode и IL2CPP не запускались; iOS C# использовал доступные managed engine references.
Очередь ограничена 50 событиями: 51-е обычное событие может вытеснить старое; ранний клик может остаться unattributed после остановки процесса.
Native MAX поддерживает скрытие, но не частичную Unity alpha; одинаковые записи timeScale=0 разных владельцев неразличимы.
При миграции dedup-marker возможна дополнительная IAP/attribution отправка; нужна серверная идемпотентность. Legacy E05 остаётся без исправления по запросу.

## How to use

Начать с [FIXES.md](Documentation~/Reviews/2026-10-05/FIXES.md): там матрица исправлений, ссылки на код, совместимость и приёмка.
В consumer сохранить SDK Settings, проверить AdMob Android App ID, экспортировать и собрать Android-проект через его wrapper.
Выполнить описанные device/lifecycle сценарии; для iOS отдельно проверить native build и конкурентные Adjust getters.
При общем игровом pause controller отключить `PauseGameWhenOffline` и использовать события модуля сети.

---

# MAX standard banner size — 2026-10-05

Implemented on `tmp/max-standard-banner-size`, created from `safety` at `e38d29c`.
`AppLovinModule.StandardBannerSizeDp` exposes 320×50 dp for phones and
728×90 dp for tablets. Banner creation now sets `IsAdaptive = false` and
explicitly calls `SetBannerWidth` before showing. Existing lifecycle,
placement, refresh and analytics behavior is preserved.

`Documentation~/README.md` includes a consumer-side UI reservation example:
use `GetBannerLayout`, fall back to the standard size while it is empty,
and account for density, orientation and reduced render resolution.
Game-specific `SceneMediationBanner` is outside this SDK. Cross-Promo code
and its 396×80 prefab, including their scaling, remain unchanged.

Independent static review approved without findings. Unity 2022.3.60f1
Roslyn compilation passed for Editor Android and Android Player with real
MAX references; source hashes remained unchanged. Only the existing CS0618
warning for `SetSdkKey` remains. Whitespace verification passed.
Evidence: ignored `Temp~/BannerSizeValidation/` (summary, compiler logs and RSPs).
Unity tests are disabled by AGENTS.md; no live Editor or device ad-layout
validation was performed. No remote push was performed.

---

# Cross-promo cap exhaustion debug flag — 2026-09-30

Implemented on `tmp/cp-force-caps-debug`, created from `safety` at `8553fb0`.
The existing `AmznGoDSDKCore` component now exposes the serialized, default-off
`Debug Force Cross Promo Caps Exhausted` toggle and matching runtime property.
Startup, the property setter and Play Mode Inspector changes forward it to CP.

The override makes CP availability false only after a nonempty configuration
fetch and while CP and AppLovin are enabled. Existing fullscreen routing and
the banner's 0.25-second check then use their normal MAX paths. JSON, creative
pools and PlayerPrefs are unchanged by the flag; disabling it restores actual
cap checks. The explicit opt-in works in ordinary QA APKs as well as the Editor.
Banner IDs, MAX initialization, no-ads, visibility and fullscreen guards remain.
Usage is documented in `Runtime/Modules/Cross-Promo/README.md`.

Independent static review approved without findings. Unity 2022.3.60f1 Roslyn
passed 24 fresh assembly builds across Android Editor/Player and MAX on/off,
plus a Core compilation with the CP define/reference excluded. Source hashes
remained unchanged during the full builds. Whitespace verification passed.
Evidence: ignored `Temp~/CrossPromoForceCapsValidation/`. Unity tests are disabled
by AGENTS.md; no live Editor or device MAX banner delivery was verified.

---

# AdMob Android App ID in SDK Settings — 2026-09-30

Added `AdMob Android App ID` after SDK Key in the AppLovin MAX section of
SDK Settings, on `tmp/applovin-admob-app-id` created from `safety` at `c737590`.

- The field persists through both Editor/Runtime settings conversions and JSON.
- Nonblank values synchronize to MAX on save, reload and before builds,
  independently of an empty or unchanged SDK Key and the active build target.
- Blank values preserve the existing MAX App ID for compatibility with old
  configurations. Raw nonblank values retain strict Android preflight validation.
- Disabled SDK/AppLovin does not copy the ID; existing Android SafeDK handling
  remains intact. No iOS App ID or runtime initialization changes were needed.
- UI help, preflight errors and README now point to the SDK Settings field.

Independent static review approved the implementation without findings.
Unity 2022.3.60f1 Roslyn compilation passed 24/24 fresh assembly builds across
Android Editor/Player and AppLovin on/off, with Analytics enabled. Source hashes
stayed unchanged. Evidence: ignored `Temp~/AdMobAppIdSettingsValidation/`.
Whitespace verification passed. Unity tests are disabled by AGENTS.md; no live
Editor UI/save/build or device validation was performed.

---

# Dev Crew report

**Date:** 2026-09-30
**Task:** Install Google AdMob with the mandatory adapters when the AppLovin module is enabled.

## What was done

- ✅ Added pinned Google AdMob Android adapter `25050000.0.0` to the required MAX set; Google Ad Manager stays excluded.
- ✅ Added Android App ID validation and setup instructions; changes prepared on `tmp/applovin-required-admob`, created from `safety`.

## Architecture

The existing AppLovin installer includes AdMob through the shared allowed-adapter policy and pinned package list.
Android preflight reads MAX settings through reflection and stops enabled SDK/AppLovin Android builds when the raw App ID is missing or malformed, without logging its value.

## Files created/modified

- `Editor/SdkDependencies/AppLovinPackageInstaller.cs` — pinned AdMob package.
- `Editor/SdkModulesSettings/ForbiddenAdNetworks.cs` — permits AdMob while retaining the Ad Manager ban.
- `Editor/SdkModulesSettings/AndroidBuildPreflight.cs` — validates MAX Android AdMob App ID.
- `README.md` — automatic installation and App ID setup instructions.
- `REPORT.md` — this entry, preserving previous reports.

## Review results

Approved after 1 review iteration; critical 0, high 0, medium 0, low 0.
Whitespace verification passed; no push or changes to `main` were performed.

## Tests

Testing disabled — skipped (tester/Unity Test Framework, per AGENTS.md).
Unity 2022.3.60f1 Roslyn compilation passed 13/13 fresh assembly builds with AppLovin on/off; source hashes remained unchanged. Only existing CS0168/CS0414/CS0618 warnings remained.
Evidence: ignored `Temp~/AdMobRequiredValidation/Summary.log` and adjacent compiler logs, response files and source hashes.

## Known limitations

No live UPM installation, Unity Editor build, Android APK or device ad-serving validation was run for this change.
The local MAX Android App ID is blank; supply your own valid ID before building.

## How to use

Enable AppLovin in `AMZN GoD > SDK Settings`, save settings and wait for automatic dependency preparation.
Set your Android AdMob App ID in `AppLovin > Integration Manager > Google Bidding and Google AdMob > App ID (Android)`, then build.

---

# Canonical advertising placement — 2026-09-30

The user clarified that `placement` itself identifies the advertising location:
`banner`, `interstitial`, or `rewarded`. Implemented the correction on
`tmp/canonical-event-placement`, created from local `safety`.

- Removed the previously added separate format field from SDK custom events,
  backend payloads and native revenue metadata, along with its helper and API
  overloads. Original public tracking signatures are restored.
- MAX analytics now derives placement from the actual show/callback path.
  Internal reporting methods do not accept configurable placement names.
  Requests, no-fill, lifecycle events, backend clicks/impressions and native
  Adjust/AppMetrica revenue all receive canonical placement values.
- Existing custom MAX placement settings remain for MAX show calls only;
  the Editor help text and documentation explain this distinction.
- Retained the cross-promo banner-click correction and MAX banner analytics:
  ad-unit filtering, mirrored subscriptions, load errors, click tracking,
  impression/revenue tracking on every revenue callback, and late revenue
  after hide. AppMetrica retains the matching native AdType.
- Legacy unknown-placement serialization, event queues, first-open attribution
  and Adjust tracking URLs remain unchanged. Manual tracking calls should pass
  the canonical placement strings documented in the SDK README.

Two independent static reviews approved the final AppLovin and
backend/CrossPromo changes without functional blockers. Line-ending findings
were resolved before compilation. Unity 2022.3.60f1 Roslyn compilation passed
**44/44 module builds** across Editor Android / Android Player × Analytics
on/off × AppLovin on/off. Runtime and dependent modules used fresh references;
all source hashes remained unchanged throughout compilation. Remaining
diagnostics were the existing CS0168/CS0414 and obsolete SetSdkKey CS0618 warnings.

Evidence: ignored `Temp~/CanonicalPlacementValidation/Summary.log`, compiler
logs, response files and source hashes. Whitespace checks passed. Unity tests
are disabled by AGENTS.md and were not run. No device/Editor play or live
analytics-delivery validation was performed. No new backend field is required.

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

---

# Dev Crew report

**Date:** 2026-09-30
**Task:** Remove repeated file and package checks from SDK Settings rendering.

## What was done

- ✅ Moved installation checks and adapter text preparation out of `OnGUI` into a window-local snapshot.
- ✅ Added event-driven refresh with a 2.1-second debounce beyond the installers' 2-second cache TTL; no idle rescanning.
- ✅ Replaced window dependency requests with registered-package inspection; preserved unsaved settings and dependency Retry.
- ✅ Architecture and code review approved; full editor assembly compilation and whitespace checks passed.
- ✅ Implemented on `tmp/sdk-settings-performance`, incorporating current `safety` (`b1ef107`).

## Architecture

`SDKSettingsWindow` renders cached package state and updates it after focus, project/package changes, saves and installer completion.
`DependencyInstaller.GetRegisteredDependenciesInstallInfo` reads `PackageInfo.GetAllRegisteredPackages` and reuses existing compatibility checks.
The window status path makes no `Client.List` request; installation and build checks retain their existing behavior.

## Files created/modified

- `Editor/Windows/SDKSettingsWindow.cs` — snapshot lifecycle, debounced refresh and cached adapter labels.
- `Editor/SdkDependencies/DependencyInstaller.cs` — read-only registered dependency status helper.
- `REPORT.md` — appended this validation record; previous reports preserved.

## Review results

Approved after 1 formal review iteration; critical/high/medium/low issues: 0/0/0/0.
Architect approved the implementation; final reviewer returned `approved=true`, `issues=[]`.

## Tests

Testing disabled — skipped (project `AGENTS.md`; no Tester agent).
Full `AMZNGoDSDK.Editor` compilation with Unity 2022.3.60f1 Roslyn passed (exit 0); source hashes remained stable and `git diff --check` passed.
Only baseline CS0168 warnings remain: `YandexDiskUploader.cs:200`, `SdkSettingsManager.cs:54`.
Evidence: original SDK `Temp~/SdkSettingsPerformanceValidation/{baseline-compile.log,final-compile.log,editor-compile.rsp,source-hashes.txt}`.

## Known limitations

Live Unity UI responsiveness and profiler timings were not measured; no quantified window speedup is claimed.
Package status refresh deliberately waits about 2.1 seconds after changes and pauses while the editor or installers are busy.

## How to use

Open `AMZN GoD > SDK Settings` in Unity; package statuses refresh automatically after relevant changes.
Save settings as usual; use the existing dependency check/retry button for installation recovery.
