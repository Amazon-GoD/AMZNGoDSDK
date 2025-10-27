Overview (ver. 1.0.1)

Overview
The ForegroundServiceManager class is a Unity script that interacts with Android’s foreground services. It provides methods for starting and stopping a foreground service, as well as requesting battery optimization permissions on Android devices.

This class is designed to work within Unity projects, and it leverages AndroidJavaClass to call Java methods from the com.infatica.agent.ForegroundServiceBridge class, which handles the Android-specific operations.

Features
Start Foreground Service: Starts a foreground service on Android with a notification to keep the service running in the background.

Request Battery Optimization Permission: Requests permission to ignore battery optimizations on Android devices to ensure uninterrupted service.

Stop Service: Stops the foreground service when no longer needed.

Requirements
Unity with Android build support.

Android API Level 23 (or higher) to request battery optimization permissions.

The Infatica SDK and the necessary native libraries (libagent.so, etc.) should be included in the Unity project for this to work correctly.


To set up ForegroundServiceManager you have to check if InfaticaSDK -> Plugins -> Android -> ForegroundService has these files
1. ForegroundServiceBridge.java
2. infatica-agent-service.aar

*please note that  infatica-agent-service.aar may contain libagent.so libs so you need to choose which one will be used in build and turn off unused libs in project or in the .aar archive*

In StartStop scene click on Canvas game object and see StartStop.cs script, to use foreground method please check Use Foreground Service flag.

To build application with  infatica-agent-service.aar you need to enable custom main gradle templete
PlayerSettings -> PublishingSettings -> custom main gradle templete
and add this line to dependencies part of gradle - implementation 'androidx.core:core:1.12.0'

In StartStop.cs you also can check how to call and work with ForegroundServiceManager.
