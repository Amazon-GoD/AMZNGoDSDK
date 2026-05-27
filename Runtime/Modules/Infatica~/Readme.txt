=============================================
Infatica SDK Unity (v2.0.0)
=============================================

Integrating the Infatica SDK into an Existing Unity Project
-----------------------------------------------------------

**Overview**
============
This manual is designed for developers integrating the **Infatica SDK** (distributed as a custom `.unitypackage`) into an existing Unity application.  
It supports **4 platforms**:
- Standalone (Windows, macOS)
- Android
- iOS

The Infatica SDK is essential for tasks that must continue running even when the app is not in the foreground, such as:
- Network operations
- Data synchronization
- Location tracking

**InfaticaAgent** is a C# interface providing simple methods to start, stop, and manage SDK services from within Unity.  
It communicates with native-side code through platform-specific libraries and wrappers.

**Features**
============
- **Start Agent** — Initiates the Infatica agent and starts it.
- **Request Battery Optimization Exemption** — Prompts the user to exclude the app from battery optimization (Android only).
- **Stop Agent** — Stops the running service and frees system resources.
- **Get Agent ID** — Returns the agent client’s ID (not available for Android).

**Code Example**
================

Start Agent (pass partner ID):
```csharp
string partnerID = "test_partner";
InfaticaAgent.Start(partnerID, () =>
{
    // do something when started
});
```

Stop Agent:
```csharp
InfaticaAgent.Stop();
```

Get Agent ID:
```csharp
string id = InfaticaAgent.GetID();
```

Request Battery Optimization Exemption:
```csharp
InfaticaAgent.AskIgnoreBatteryOptimization();
```

**Requirements**
================
- Unity Editor **2022.3.x or higher**
- Minimum Android API Level **24 (Android 7.0)**
- Infatica SDK integrated into the Unity project
- (Not tested on Unity 6)

---------------------------------------------
Android Setup (Min API 24)
---------------------------------------------
1. **Verify Required Files**
   - Navigate to `Assets/InfaticaSDK/Plugins/Android`
   - Confirm the following are present:
     - `ForegroundServiceBridge.java`
     - `infatica-agent-service.aar` (contains `libagent.so` for x86, x86_64, arm64-v8a, armeabi-v7a)

   ⚠️ Ensure **only one version** of native libraries is included in the final APK.

2. **Unity Scene Configuration**
   - Open scene: `Assets/InfaticaSDK/Demos/StartStopMobile`
   - Select the `StartStop` GameObject and check `StartStop.cs` — ensure all fields are filled.

3. **Gradle Configuration**
   - If using a custom `mainTemplate.gradle`, add this dependency:
     ```gradle
     implementation 'androidx.core:core:1.12.0'
     ```

---------------------------------------------
Standalone (Windows)
---------------------------------------------
1. **Verify Required Files**
   - Path: `Assets/InfaticaSDK/Plugins/Windows/x64` or `x86`
   - Files:
     - `infatica_agent.dll`
     - `infatica_agent.h`

2. **Unity Scene Configuration**
   - Open scene: `Assets/InfaticaSDK/Demos/StartStopStandalone`
   - Select `StartStop` GameObject and verify `StartStop.cs` fields.

---------------------------------------------
Standalone (macOS)
---------------------------------------------
1. **Verify Required Files**
   - Path: `Assets/InfaticaSDK/Plugins/MacOS`
   - Files:
     - `infatica_sdk.dylib`
     - `infatica_sdk.h`

2. **Unity Scene Configuration**
   - Scene: `Assets/InfaticaSDK/Demos/StartStopStandalone`
   - Verify `StartStop.cs` fields.

3. **Testing in Editor**
   - Sign `.dylib` for local use:
     ```bash
     cd "path to project"/Assets/InfaticaSDK/Plugins/MacOS
     xattr -dr com.apple.quarantine infatica_sdk.dylib
     codesign --force --deep --sign - --timestamp=none infatica_sdk.dylib
     ```

4. **Building**
   - In Unity Build Settings, ensure *Create Xcode Project* is enabled.
   - Sign with your developer certificate.

---------------------------------------------
iOS
---------------------------------------------
1. **Verify Required Files**
   - Path: `Assets/InfaticaSDK/Plugins/IOS`
   - Files:
     - `InfaticaWrapper.c`
     - `libagent.a`
     - `libaget.h`
     - `PermissionBridge.mm`

2. **Unity Scene Configuration**
   - Scene: `Assets/InfaticaSDK/Demos/StartStopMobile`
   - Verify `StartStop.cs` fields.

3. **Permission Service**
   - Path: `Assets/InfaticaSDK/Scripts/IOS/PermissionService.cs`
   - Use this to request or check location permission.

4. **Info.plist Auto Configuration**
   - File: `Assets/Editor/PostProcessBuild.cs`
   - Automatically adds:
     - `NSLocationWhenInUseUsageDescription`
     - `NSLocationAlwaysAndWhenInUseUsageDescription`

5. **Build**
   - Sign the app with a developer certificate and build — no extra steps needed.

---------------------------------------------
Example Usage
---------------------------------------------
In `StartStop.cs`, you can:
- Initialize and start the agent
- Stop the agent via UI
- Request Android battery optimization exclusion
- Connect UI toggles to service states

---------------------------------------------
License
---------------------------------------------
This component is part of the **proprietary Infatica SDK**.  
For licensing, distribution, or technical details, contact **Infatica**.
