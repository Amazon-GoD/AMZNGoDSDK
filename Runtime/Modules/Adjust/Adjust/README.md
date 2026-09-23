# Adjust vendored integration

Source: [Adjust Unity SDK v5.8.0](https://github.com/adjust/unity_sdk/tree/v5.8.0).

- Unity wrapper: 5.8.0; upstream supports Unity 2019.4 and newer.
- Android SDK: 5.8.0; its AAR declares minimum Android API 21. The complete AMZNGoDSDK mediation stack requires API 24.
- Android signature library: 5.0.0, provided by the native SDK's Maven dependency.
- iOS SDK: 5.8.0; minimum iOS deployment target 12.0.

The dependency XML remains a template and is activated by AMZNGoDSDK only when the Adjust module is enabled. Existing Unity asset GUIDs, module assembly constraints and manual initialization behavior are preserved.

The upstream MIT license is included in `LICENSE.md`.
