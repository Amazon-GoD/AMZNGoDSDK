# Changelog

All notable changes to the AMZN GoD SDK package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-08-21

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
- Dependencies audit: the package now depends only on `com.unity.ugui` and
  `com.unity.textmeshpro`. `com.unity.purchasing` and
  `com.unity.services.core` were removed — the SDK does not reference them
  (in-app purchases use the vendored Amazon Appstore IAP SDK).

### Removed

- `DependencyPreprocessor` (moved XML files inside the SDK folder during
  builds — incompatible with immutable packages), replaced by the EDM
  template generator.
- `ModuleFolderManager`, `ModuleFolderWindow`, `ModuleFilesWrapper`,
  `AutoWrapAllFiles` (folder-rename toggle machinery).
