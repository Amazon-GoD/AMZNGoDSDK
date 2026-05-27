using System;
using System.Runtime.InteropServices;
using UnityEngine;
using AOT;

public static class InfaticaWrapperIOS
{
#if UNITY_IOS && !UNITY_EDITOR
    private const string LIB = "__Internal"; // Required for iOS

    [DllImport(LIB)]
    private static extern void BeginBackgroundTask();

    [DllImport(LIB)]
    private static extern void StartLocation();

    [DllImport(LIB)]
    private static extern void EndBackgroundTask();

    [DllImport(LIB)]
    private static extern void StartAgent(string partnerId);

    [DllImport(LIB)]
    private static extern void StopAgent(int timout);

    [DllImport(LIB)]
    private static extern IntPtr GetAgentId();

    public static void StartAgentFull(string partnerId) => StartAgent(partnerId);
    public static void StopAgentFull(int timout) => StopAgent(timout);
    public static void BeginBackgroundTaskIfNeeded() => BeginBackgroundTask();
    public static void EndBackgroundTaskIfNeeded() => EndBackgroundTask();
    public static void StartPermissionLocation() => StartLocation();
    public static string GetAgentIdFull()
    {
        IntPtr ptr = GetAgentId();
        return Marshal.PtrToStringUTF8(ptr);
    }
#endif
}
