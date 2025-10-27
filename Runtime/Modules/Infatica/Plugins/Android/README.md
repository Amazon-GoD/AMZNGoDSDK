Overview (ver. 1.0.0)
The InfaticaSDK_Demo is a Unity plugin designed to wrap and expose functions from native libraries (such as .so for Android and .dll for Windows) for use within Unity C# scripts. This plugin provides methods to interact with the -libagent library, which includes functions for managing the agent's lifecycle, starting and stopping the agent, and handling network-related callbacks.

The main interface to the native library is encapsulated in the InfaticaWrapper static class, offering easy access to the library functions.

Exposed Functions
The following functions are exposed via P/Invoke from the native libagent library:

-InfaticaWrapper.InfaticaAgentCreate
-InfaticaWrapper.InfaticaAgentDestroy
-InfaticaWrapper.InfaticaAgentId
-InfaticaWrapper.InfaticaAgentStart
-InfaticaWrapper.InfaticaAgentStop
-InfaticaWrapper.InfaticaAgentNetworksThread

Example Usage
The example usage is presented in AgentController.cs.

Unity Project Setup
Follow the steps below to integrate and configure the InfaticaSDK_Demo into your Unity project.

1. Import Native Libraries
Make sure you have the following native libraries available in your Unity project:

Android: .so files (e.g., libagent.so)
Windows: .dll files (e.g., libagent.dll)

Place these libraries in the appropriate folders under the Plugins directory:

For Android:
Place the .so files in Assets/InfaticaSDK/Plugins/Android/.

2. Set Up Platform Switching
To ensure that the right library is loaded for each platform:

Android:
Make sure the .so libraries are located in the correct platform folder (Assets/Plugins/Infatica/Android/).
Unity will automatically load the appropriate libraries depending on the platform you're building for. But it is also important to look into lib platform target.

3. Configure IL2CPP and Scripting Backend
Unity's IL2CPP scripting backend can sometimes have issues with loading native libraries. Follow these steps to ensure smooth integration:

Set the Scripting Backend to IL2CPP:

Go to Edit → Project Settings → Player.
Under Other Settings, set Scripting Backend to IL2CPP.
Configure API Compatibility Level:

Go to Edit → Project Settings → Player.
In Other Settings, set the API Compatibility Level to .NET Standard 2.1 (or the appropriate version for your project).

4. Set Target Architectures for Android
For Android, make sure the following architectures are supported:

armeabi-v7a
arm64-v8a
You can set these architectures in Edit → Project Settings → Player → Other Settings → Target Architectures.

5. Building and Platform Switching
When building your Unity project, Unity will automatically switch to the correct platform and load the corresponding native libraries (.so for Android, .dll for Windows). If you encounter any issues with platform switching, make sure you:

Use Platform Dependent Compilation in your code (e.g., using #if UNITY_ANDROID).
Ensure you are building for the correct target platform under File → Build Settings.

You can use InfatikaSDK_Demo as example of how work with native libraries.