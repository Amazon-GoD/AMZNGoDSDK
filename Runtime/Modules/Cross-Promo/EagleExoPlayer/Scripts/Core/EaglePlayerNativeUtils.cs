/*==============================================================================
Copyright (c) 2016 By XiangKuiZheng, Inc.
All Rights Reserved.
==============================================================================*/

using UnityEngine;
using System.Collections;

/// <summary>
/// Pico unity activity.
/// </summary>
public class EaglePlayerNativeUtils
{
	private static AndroidJavaObject currentEagleUtils;
    private static AndroidJavaObject CurrentEagleUtils ()
    {
        if (Application.platform == RuntimePlatform.Android) {
			string className = "com.eagle.lib.EaglePlayerUtils";
            if (currentEagleUtils == null) {
				currentEagleUtils =new AndroidJavaObject (className); 
            }
        }

        return currentEagleUtils;
    }

    public static bool CallObjectMethod (string name, params object[] args)
    {
        if (CurrentEagleUtils () == null) {
            Debug.LogError ("Object is null when calling method " + name);
            return false;
        }
        try {
            CurrentEagleUtils ().Call (name, args);
            return true;
        } catch (AndroidJavaException e) {
            Debug.LogError ("Exception calling method " + name + ": " + e);
            return false;
        }
    }
        
    public static bool CallObjectMethod<T> (ref T result, string name)
    {
        if (CurrentEagleUtils () == null) {
            Debug.LogError ("Object is null when calling method " + name);
            return false;
        }
        try {
            result = CurrentEagleUtils ().Call<T> (name);
            return true;
        } catch (AndroidJavaException e) {
            Debug.LogError ("Exception calling method " + name + ": " + e);
            return false;
        }
    }

    public static bool CallObjectMethod<T> (ref T result, string name,
        params object[] args)
    {
        if (CurrentEagleUtils () == null) {
            Debug.LogError ("Object is null when calling method " + name);
            return false;
        }
        try {
            result = CurrentEagleUtils ().Call<T> (name, args);
            return true;
        } catch (AndroidJavaException e) {
            Debug.LogError ("Exception calling method " + name + ": " + e);
            return false;
        }
    }

    /// <summary>
    /// Get the target Activity
    /// </summary>
    /// <param name="package_name">Package Name</param>
    /// <param name="activity_name">Activity Name</param>
    /// <returns>activity Object</returns>
    public static AndroidJavaObject GetActivity (string package_name, string activity_name)
    {
        return new AndroidJavaClass (package_name).GetStatic<AndroidJavaObject> (activity_name);
    }

}
