# [AppMetrica Unity Plugin](https://appmetrica.io)

AppMetrica is a free real-time ad tracking and mobile app analytics solution. 
AppMetrica covers the three key features for discovering your app's performance — ad tracking, usage analytics and crash analytics.
Detailed information and instructions for integration are available in the [documentation](https://appmetrica.io/docs/en/sdk/unity/analytics/quick-start).

The plugin is available for Android and iOS and includes native AppMetrica SDKs:

- AppMetrica SDK for Android [8.5.1](https://appmetrica.io/docs/en/sdk/android/changelog-android#s-8-5-1).
- AppMetrica SDK for iOS [6.3.0](https://appmetrica.io/docs/en/sdk/ios/changelog-ios#v-6-3-0).

## Documentation

Documentation could be found at [AppMetrica official site](https://appmetrica.io/docs/en/sdk/unity/analytics/quick-start).

## Changelog

Changelog could be found at [AppMetrica official site](https://appmetrica.io/docs/en/sdk/unity/changelog#sdk).

## Dependency resolver

The plugin uses [EDM4U](https://github.com/googlesamples/unity-jar-resolver) to resolve native dependencies for Android and iOS.

## Vendored integration

Unity plugin source: [6.10.0](https://github.com/appmetrica/appmetrica-unity-plugin/tree/v6.10.0). Android SDK 8.5.1 is pinned for the upstream OkHttp connection leak fix; its AAR declares minimum Android API 21. AMZNGoDSDK requires API 24 for the complete mediation stack. iOS pods follow upstream 6.10.0 requirements.

Native dependencies are generated from templates only when the module is enabled. Local Unity asset GUIDs, assembly constraints, Android bridge import settings and module integration are preserved.
