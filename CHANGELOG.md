# Changelog

All notable changes to the AMZN GoD SDK package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
