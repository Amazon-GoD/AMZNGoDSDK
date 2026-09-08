
// #if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

public class UniWebViewAndroidStaticListener: MonoBehaviour {
    void Awake() {
        DontDestroyOnLoad(gameObject);
    }

    void OnJavaMessage(string message) {
        
        UniWebViewLogger.Instance.Verbose(
            "Received message sent from native: " + message
        );
        
        // {listener_name}@{method_name}@parameters
        var parts = message.Split("@"[0]);
        if (parts.Length < 3) {
            Debug.Log("Not enough parts for receiving a message.");
            return;
        }

        var listener = UniWebViewNativeListener.GetListener(parts[0]);
        if (listener == null) {
            return;
        }
        
        // Check if the listener's host reference is null before invoking callbacks
        // This prevents crashes when native callbacks arrive after Unity object destruction
        if (listener.webView == null && listener.safeBrowsing == null && listener.session == null) {
            UniWebViewLogger.Instance.Debug(
                "Ignored message for destroyed webView. Listener: " + parts[0] + 
                " Method: " + parts[1]
            );
            return;
        }
        
        MethodInfo methodInfo = typeof(UniWebViewNativeListener).GetMethod(parts[1]);
        if (methodInfo == null) {
            Debug.Log("Cannot find correct method to invoke: " + parts[1]);
            return;
        }
        
        var leftLength = parts.Length - 2;
        var left = new string[leftLength];
        for (int i = 0; i < leftLength; i++) {
            left[i] = parts[i + 2];
        }
        
        try {
            methodInfo.Invoke(listener, new object[] { string.Join("@", left) });
        } catch (System.Exception e) {
            // Additional safety: Log and ignore exceptions from destroyed objects
            UniWebViewLogger.Instance.Critical(
                "Exception in OnJavaMessage callback - Listener: " + parts[0] + 
                " Method: " + parts[1] + " Error: " + e.Message
            );
        }
    }
}

// #endif