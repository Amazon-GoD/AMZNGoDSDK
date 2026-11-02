//
//  UniWebViewMainThreadDispatcher.cs
//  Created by Wang Wei(@onevcat) on 2025-08-27.
//
//  This file is a part of UniWebView Project (https://uniwebview.com)
//  By purchasing the asset, you are allowed to use this code in as many as projects 
//  you want, only if you publish the final products under the name of the same account
//  used for the purchase. 
//
//  This asset and all corresponding files (such as source code) are provided on an 
//  "as is" basis, without warranty of any kind, express of implied, including but not
//  limited to the warranties of merchantability, fitness for a particular purpose, and 
//  noninfringement. In no event shall the authors or copyright holders be liable for any 
//  claim, damages or other liability, whether in action of contract, tort or otherwise, 
//  arising from, out of or in connection with the software or the use of other dealing in the software.
//

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A main thread dispatcher for UniWebView to ensure callbacks happen on Unity's main thread.
/// This is particularly important for Android where JavascriptInterface callbacks occur on JavaBridge thread.
/// </summary>
public class UniWebViewMainThreadDispatcher : MonoBehaviour {
    
    private static UniWebViewMainThreadDispatcher _instance;
    private readonly Queue<Action> _executionQueue = new Queue<Action>();
    private readonly object _queueLock = new object();
    
    /// <summary>
    /// Gets or creates the singleton instance of the main thread dispatcher.
    /// </summary>
    public static UniWebViewMainThreadDispatcher Instance {
        get {
            if (_instance == null) {
                // Try to find existing instance
                _instance = FindFirstObjectByType<UniWebViewMainThreadDispatcher>();
                
                if (_instance == null) {
                    // Create new GameObject with dispatcher
                    var go = new GameObject("UniWebViewMainThreadDispatcher");
                    _instance = go.AddComponent<UniWebViewMainThreadDispatcher>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }
    
    void Awake() {
        if (_instance != null && _instance != this) {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }
    
    void Update() {
        // Process actions queued from background threads
        lock (_queueLock) {
            while (_executionQueue.Count > 0) {
                var action = _executionQueue.Dequeue();
                try {
                    action?.Invoke();
                } catch (Exception e) {
                    UniWebViewLogger.Instance.Critical(() => $"Error executing action on main thread: {e.Message}");
                }
            }
        }
    }
    
    /// <summary>
    /// Enqueues an action to be executed on the main thread.
    /// </summary>
    /// <param name="action">The action to execute on the main thread</param>
    public void Enqueue(Action action) {
        if (action == null) {
            return;
        }
        
        lock (_queueLock) {
            _executionQueue.Enqueue(action);
        }
    }
    
    /// <summary>
    /// Executes a function on the main thread and returns the result.
    /// This method blocks until the function completes.
    /// </summary>
    /// <typeparam name="T">Return type of the function</typeparam>
    /// <param name="func">The function to execute</param>
    /// <param name="timeoutSeconds">Timeout in seconds (default: 5)</param>
    /// <returns>The result of the function</returns>
    public T ExecuteOnMainThread<T>(Func<T> func, float timeoutSeconds = 5f) {
        if (func == null) {
            return default(T);
        }
        
        // If we're already on the main thread, execute directly
        if (IsMainThread()) {
            return func();
        }
        
        T result = default(T);
        bool completed = false;
        Exception exception = null;
        
        Enqueue(() => {
            try {
                result = func();
            } catch (Exception e) {
                exception = e;
            } finally {
                completed = true;
            }
        });
        
        // Wait for completion with timeout using thread-safe DateTime
        var startTime = System.DateTime.Now;
        while (!completed) {
            if ((System.DateTime.Now - startTime).TotalSeconds > timeoutSeconds) {
                UniWebViewLogger.Instance.Critical(() => $"Main thread execution timeout after {timeoutSeconds} seconds");
                break;
            }
            System.Threading.Thread.Sleep(1);
        }
        
        if (exception != null) {
            throw exception;
        }
        
        return result;
    }
    
    /// <summary>
    /// Checks if the current thread is Unity's main thread.
    /// This is a heuristic based on thread ID, which works for most cases.
    /// </summary>
    private static bool IsMainThread() {
        // Unity's main thread typically has ID 1, but this is not guaranteed
        // A more robust check would require platform-specific code
        return System.Threading.Thread.CurrentThread.ManagedThreadId == 1;
    }
    
    void OnDestroy() {
        if (_instance == this) {
            _instance = null;
        }
    }
}