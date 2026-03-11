using System;
using System.Runtime.InteropServices;
using UnityEngine;

public static class PermissionService
{
#if UNITY_IOS && !UNITY_EDITOR
    private const string LIB = "__Internal";

    [DllImport(LIB)]
    private static extern void RequestLocationPermission();

    [DllImport(LIB)]
    private static extern int GetLocationPermissionStatus();

    public enum LocationStatus
    {
        NotDetermined = 0,
        Restricted = 1,
        Denied = 2,
        AuthorizedAlways = 3,
        AuthorizedWhenInUse = 4,
        Authorized = 5
    }

    public static void AskLocationPermission()
    {
        RequestLocationPermission();
    }

    public static LocationStatus GetStatus()
    {
        return (LocationStatus)GetLocationPermissionStatus();
    }

#endif
}