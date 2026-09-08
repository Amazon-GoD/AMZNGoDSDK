//
//  UniWebView.cs
//  Created by Wang Wei (@onevcat) on 2017-04-11.
//
//  This file is a part of UniWebView Project (https://uniwebview.com)
//  By purchasing the asset, you are allowed to use this code in as many as projects 
//  you want, only if you publish the final products under the name of the same account
//  used for the purchase. 
//
//  This asset and all corresponding files (such as source code) are provided on an 
//  “as is” basis, without warranty of any kind, express of implied, including but not
//  limited to the warranties of merchantability, fitness for a particular purpose, and 
//  noninfringement. In no event shall the authors or copyright holders be liable for any 
//  claim, damages or other liability, whether in action of contract, tort or otherwise, 
//  arising from, out of or in connection with the software or the use of other dealing in the software.
//
using UnityEngine;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using UniWebViewExternal;

/// <summary>
/// Main class of UniWebView. Any `GameObject` instance with this script can represent a webview object in the scene. 
/// Use this class to create, load, show and interact with a general-purpose web view.
/// </summary>
public partial class UniWebView: MonoBehaviour {
    
#region Serialize Fields
    
    // SerializeField that shows in Unity

    [SerializeField]
    private string urlOnStart;
    [SerializeField]
    private bool showOnStart = false;

    [SerializeField]
    private bool fullScreen;

    [SerializeField]
    private bool useEmbeddedToolbar;
    
    [SerializeField]
    private UniWebViewToolbarPosition embeddedToolbarPosition;

    [SerializeField]
    private Rect frame;

    [SerializeField]
    private RectTransform referenceRectTransform;
#endregion

#region Private Fields
    private string id = Guid.NewGuid().ToString();
    private UniWebViewNativeListener listener;
    private ScreenOrientation currentOrientation;
    private Rect currentUnmappedFrame;

    // Action callback holders

    private readonly Dictionary<String, Action> actions
        = new Dictionary<String, Action>();
    private readonly Dictionary<String, Action<UniWebViewNativeResultPayload>> payloadActions
        = new Dictionary<String, Action<UniWebViewNativeResultPayload>>();
    private static readonly Dictionary<String, Action<UniWebViewNativeResultPayload>> globalPayloadActions
        = new Dictionary<String, Action<UniWebViewNativeResultPayload>>(); 


    private bool started;
    private bool backButtonEnabled = true;
    private UniWebViewCornerRadius cornerRadius = UniWebViewCornerRadius.Zero;
#endregion

#region Unity Events

    void Awake() {
        var listenerObject = new GameObject(id);
        listener = listenerObject.AddComponent<UniWebViewNativeListener>();
        listenerObject.transform.parent = transform;
        listener.webView = this;
        UniWebViewNativeListener.AddListener(listener);

        EmbeddedToolbar = new UniWebViewEmbeddedToolbar(listener);

        var rect = fullScreen ? new Rect(0, 0, Screen.width, Screen.height) : NextFrameRect();

        UniWebViewInterface.Init(listener.Name, (int)rect.x, (int)rect. y, (int)rect.width, (int)rect.height);
        currentOrientation = Screen.orientation;
        currentUnmappedFrame = UnmappedFrame();
        
        // Register channel message handler
        RegisterChannelMessageHandler();
    }

    void Start() {
        if (showOnStart) {
            Show();
        }
        
        if (useEmbeddedToolbar) {
            EmbeddedToolbar.SetPosition(embeddedToolbarPosition);
            EmbeddedToolbar.Show();            
        }

        if (!string.IsNullOrEmpty(urlOnStart)) {
            Load(urlOnStart);
        }
        started = true;
        if (referenceRectTransform != null) {
            UpdateFrame();
        }
    }

    void Update() {
        var newOrientation = Screen.orientation;
        var needUpdateFrame = false;
        if (currentOrientation != newOrientation) {
            currentOrientation = newOrientation;
            if (OnOrientationChanged != null) {
                OnOrientationChanged(this, newOrientation);
            }
            if (referenceRectTransform != null) {
                needUpdateFrame = true;
            }
        }
        
        // The case that the screen size changes but the orientation does not change.
        // (Fold screen expanding, for example)
        var nextUnmappedFrame = UnmappedFrame();
        if (!currentUnmappedFrame.Equals(nextUnmappedFrame)) {
            currentUnmappedFrame = nextUnmappedFrame;
            needUpdateFrame = true;
        }
        
        if (needUpdateFrame) {
            UpdateFrame();
        }

        // Only the new input system is enabled. Related flags: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.0/manual/Installation.html#enabling-the-new-input-backends
        //
        // The new input system is not handling touchscreen events nicely as the old one. 
        // The gesture detection hangs out regularly. Wait for an improvement of Unity.
        // So we choose to use the old one whenever it is available.
        #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var backDetected = backButtonEnabled && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
        #else
        var backDetected = backButtonEnabled && Input.GetKeyUp(KeyCode.Escape);
        #endif

        if (backDetected) {
            UniWebViewLogger.Instance.Info(() => "Received Back button, handling GoBack or close web view.");
            if (CanGoBack) {
                GoBack();
            } else {
                InternalOnShouldClose();
            }
        }
    }

    void OnEnable() {
        if (started) {
            _Show(useAsync: true);
        }
    }

    void OnDisable() {
        if (started) {
            _Hide(useAsync: true);
        }
    }

        
    void OnDestroy() {
        UniWebViewNativeListener.RemoveListener(listener.Name);
        UniWebViewChannelMethodManager.Instance.UnregisterChannel(listener.Name);
        UniWebViewInterface.Destroy(listener.Name);
        Destroy(listener.gameObject);
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    // From Unity 2022.3.10, the player view is brought to front when switching back from a pause
    // state. Requiring to bring the web view to front to make the web view visible.
    // Issue caused by:
    // https://issuetracker.unity3d.com/issues/android-a-black-screen-appears-for-a-few-seconds-when-returning-to-the-game-from-the-lock-screen-after-idle-time
    // 
    // Ref: UWV-1061
    void OnApplicationPause(bool pauseStatus) {
        if (RestoreViewHierarchyOnResume && !pauseStatus) {
            UniWebViewInterface.BringContentToFront(listener.Name);
        }
    }
#endif

#endregion

#region Capabilities

    /// <summary>
    /// Whether the web view is supported in current runtime or not.
    /// 
    /// On some certain Android customized builds, the manufacturer prefers not containing the web view package in the 
    /// system or blocks the web view package from being installed. If this happens, using of any web view related APIs will
    /// throw a `MissingWebViewPackageException` exception.
    /// 
    /// Use this method to check whether the web view is available on the current running system. If this parameter returns `false`, 
    /// you should not use the web view.
    /// 
    /// This property always returns `true` on other supported platforms, such as iOS or macOS editor. It only performs 
    /// runtime checking on Android. On other not supported platforms such as Windows or Linux, it always returns `false`.
    /// </summary>
    /// <value>Returns `true` if web view is supported on the current platform. Otherwise, `false`.</value>
    public static bool IsWebViewSupported {
        get {
            #if UNITY_EDITOR_OSX
            return true;
            #elif UNITY_EDITOR
            return false;
            #elif UNITY_IOS
            return true;
            #elif UNITY_STANDALONE_OSX
            return true;
            #elif UNITY_ANDROID
            return UniWebViewInterface.IsWebViewSupported();
            #else
            return false; 
            #endif
        }
    }

    /// <summary>
    /// Gets the height of the status bar in pixels.
    /// 
    /// When Unity is configured to render outside the safe area (i.e., full screen mode), this method returns 0.
    /// When the status bar is visible, it returns the actual pixel height of the status bar.
    /// 
    /// On Android, it detects fullscreen and immersive modes to determine if the status bar is hidden.
    /// 
    /// This method always returns 0 on iOS, macOS and other platforms for compatibility reasons.
    /// </summary>
    /// <returns>The status bar height in pixels on Android, or 0 on other platforms.</returns>
    private static int GetStatusBarHeight() {
        #if UNITY_ANDROID && !UNITY_EDITOR
        return UniWebViewInterface.GetStatusBarHeight();
        #else
        return 0;
        #endif
    }

    /// <summary>
    /// Sets whether the link clicking in the web view should open the page in an external browser.
    /// </summary>
    /// <param name="flag">The flag indicates whether a link should be opened externally.</param>
    public void SetOpenLinksInExternalBrowser(bool flag) {
        UniWebViewInterface.SetOpenLinksInExternalBrowser(listener.Name, flag);
    }

    /// <summary>
    /// Sets whether JavaScript should be enabled in current web view. Default is enabled.
    /// </summary>
    /// <param name="enabled">Whether JavaScript should be enabled.</param>
    public static void SetJavaScriptEnabled(bool enabled) {
        UniWebViewInterface.SetJavaScriptEnabled(enabled);
    }

    /// <summary>
    /// Sets allow auto play for current web view. By default, 
    /// users need to touch the play button to start playing a media resource.
    /// 
    /// By setting this to `true`, you can start the playing automatically through
    /// corresponding media tag attributes.
    /// </summary>
    /// <param name="flag">A flag indicates whether autoplaying of media is allowed or not.</param>
    public static void SetAllowAutoPlay(bool flag) {
        UniWebViewInterface.SetAllowAutoPlay(flag);
    }

    /// <summary>
    /// Sets allow inline play for current web view. By default, on iOS, the video 
    /// can only be played in a new full screen view.
    /// By setting this to `true`, you could play a video inline the page, instead of opening 
    /// a new full screen window.
    /// 
    /// This only works for iOS and macOS Editor. 
    /// On Android, you could play videos inline by default and calling this method does nothing.
    /// </summary>
    /// <param name="flag">A flag indicates whether inline playing of media is allowed or not.</param>
    public static void SetAllowInlinePlay(bool flag) {
        #if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR_WIN && !UNITY_EDITOR_LINUX
        UniWebViewInterface.SetAllowInlinePlay(flag);
        #endif
    }

    /// <summary>
    /// Sets whether JavaScript can open windows without user interaction.
    /// 
    /// By setting this to `true`, an automatically JavaScript navigation will be allowed in the web view.
    /// </summary>
    /// <param name="flag">Whether JavaScript could open window automatically.</param>
    public static void SetAllowJavaScriptOpenWindow(bool flag) {
        UniWebViewInterface.SetAllowJavaScriptOpenWindow(flag);
    }

    /// <summary>
    /// Sets whether the web page console output should be forwarded to native console.
    ///
    /// By setting this to `true`, UniWebView will try to intercept the web page console output methods and forward
    /// them to the native console, for example, Xcode console on iOS, Android logcat on Android and Unity Console when
    /// using Unity Editor on macOS. It provides a way to debug the web page by using the native console without opening
    /// the web inspector. The forwarded logs in native side contains a "&lt;UniWebView-Web&gt;" tag. 
    ///
    /// Default is `false`. You need to set it before you create a web view instance to apply this setting. Any existing
    /// web views are not affected.
    ///
    /// Logs from the methods below will be forwarded:
    /// 
    /// - console.log
    /// - console.warn
    /// - console.error
    /// - console.debug
    /// 
    /// </summary>
    /// <param name="flag">Whether the web page console output should be forwarded to native output.</param>
    public static void SetForwardWebConsoleToNativeOutput(bool flag) {
        UniWebViewInterface.SetForwardWebConsoleToNativeOutput(flag);
    }

    /// <summary>
    /// Sets the way of how the cache is used when loading a request.
    ///
    /// The default value is `UniWebViewCacheMode.Default`.
    /// </summary>
    /// <param name="cacheMode">The desired cache mode which the following request loading should be used.</param>
    public void SetCacheMode(UniWebViewCacheMode cacheMode) {
        UniWebViewInterface.SetCacheMode(listener.Name, (int)cacheMode);
    }

    /// <summary>
    /// Sets whether the device back button should be enabled to execute "go back" or "closing" operation.
    /// 
    /// On Android, the device back button in navigation bar will navigate users to a back page. If there is 
    /// no any back page available, the back button clicking will try to raise a `OnShouldClose` event and try 
    /// to close the web view if `true` is return from the event. If the `OnShouldClose` is not listened, 
    /// the web view will be closed and the UniWebView component will be destroyed to release using resource.
    /// 
    /// Listen to `OnKeyCodeReceived` if you need to disable the back button, but still want to get the back 
    /// button key pressing event.
    /// 
    /// Default is enabled.
    /// </summary>
    /// <param name="enabled">Whether the back button should perform go back or closing operation to web view.</param>
    public void SetBackButtonEnabled(bool enabled) {
        this.backButtonEnabled = enabled;
    }

    /// <summary>
    /// /// Sets whether the web view should enable support for the "viewport" HTML meta tag or should use a wide viewport.
    /// </summary>
    /// <param name="flag">Whether to enable support for the viewport meta tag.</param>
    public void SetUseWideViewPort(bool flag) {
        #if UNITY_ANDROID && !UNITY_EDITOR
        UniWebViewInterface.SetUseWideViewPort(listener.Name, flag);
        #endif
    } 

    /// <summary>
    /// Sets whether the web view loads pages in overview mode, that is, zooms out the content to fit on screen by width. 
    /// 
    /// This method is only for Android. Default is disabled.
    /// </summary>
    /// <param name="flag"></param>
    public void SetLoadWithOverviewMode(bool flag) {
        #if UNITY_ANDROID && !UNITY_EDITOR
        UniWebViewInterface.SetLoadWithOverviewMode(listener.Name, flag);
        #endif
    }

    /// <summary>
    /// Enables debugging of web contents. You could inspect of the content of a 
    /// web view by using a browser development tool of Chrome for Android or Safari for macOS.
    /// 
    /// This method is only for Android and macOS Editor. On iOS, you do not need additional step. 
    /// You could open Safari's developer tools to debug a web view on iOS.
    /// </summary>
    /// <param name="enabled">Whether the content debugging should be enabled.</param>
    public static void SetWebContentsDebuggingEnabled(bool enabled) {
        UniWebViewInterface.SetWebContentsDebuggingEnabled(enabled);
    }

    /// <summary>
    /// Sets whether a callout (context) menu should be displayed when user long tapping on certain web view content.
    /// 
    /// When enabled, when user long presses an image or link in the web page, a context menu would be show up to ask 
    /// user's action. On iOS, it is a action sheet to ask whether opening the target link or saving the image. On 
    /// Android it is a pop up dialog to ask whether saving the image to local disk. On iOS, the preview page triggered 
    /// by force touch on iOS is also considered as a callout menu.
    /// 
    /// Default is `true`, means that the callout menu will be displayed. Call this method with `false` to disable 
    /// it on the web view.
    /// </summary>
    /// <param name="enabled">
    /// Whether a callout menu should be displayed when user long pressing or force touching a certain web page element.
    /// </param>
    public void SetCalloutEnabled(bool enabled) {
        UniWebViewInterface.SetCalloutEnabled(listener.Name, enabled);
    }

    /// <summary>
    /// Sets whether the web view should support a pop up web view triggered by user in a new tab.
    /// 
    /// In a general web browser (such as Google Chrome or Safari), a URL with `target="_blank"` attribute is intended 
    /// to be opened in a new tab. However, in the context of web view, there is no way to handle new tabs without 
    /// proper configurations. Due to that, by default UniWebView will ignore the `target="_blank"` and try to open 
    /// the page in the same web view if that kind of link is pressed.
    /// 
    /// It works for most cases, but if this is a problem to your app logic, you can change this behavior by calling 
    /// this method with `enabled` set to `true`. It enables the "opening in new tab" behavior in a limited way, by 
    /// adding the new tab web view above to the current web view, with the same size and position. When the opened new 
    /// tab is closed, it will be removed from the view hierarchy automatically.
    /// 
    /// By default, only user triggered action is allowed to open a new window for security reason. That means, if you 
    /// are using some JavaScript like `window.open`, unless you set `allowJavaScriptOpening` to `true`, it won't work. 
    /// This default behavior prevents any other third party JavaScript code from opening a window arbitrarily.
    /// 
    /// </summary>
    /// <param name="enabled">
    /// Whether to support multiple windows. If `true`, the `target="_blank"` link will be opened in a new web view.
    /// Default is `false`.
    /// </param>
    /// <param name="allowJavaScriptOpening">
    /// Whether to support open the new window with JavaScript by `window.open`. Setting this to `true` means any JavaScript
    /// code, even from third party (in an iframe or a library on the page), can open a new window. Use it as your risk.
    /// </param>
    public void SetSupportMultipleWindows(bool enabled, bool allowJavaScriptOpening) {
        UniWebViewInterface.SetSupportMultipleWindows(listener.Name, enabled, allowJavaScriptOpening);
    }

    /// <summary>
    /// Sets whether the drag interaction should be enabled on iOS.
    /// 
    /// From iOS 11, the web view on iOS supports the drag interaction when user long presses an image, link or text.
    /// Setting this to `false` would disable the drag feather on the web view.
    ///
    /// On Android, there is no direct native way to disable drag only. This method instead disables the long touch
    /// event, which is used as a trigger for drag interaction. So, setting this to `false` would disable the long
    /// touch gesture as a side effect. 
    /// 
    /// It does nothing on macOS editor. Default is `true`, which means drag interaction is enabled if the device and
    /// system version supports it.
    /// </summary>
    /// <param name="enabled">
    /// Whether the drag interaction should be enabled.
    /// </param>
    public void SetDragInteractionEnabled(bool enabled) {
        UniWebViewInterface.SetDragInteractionEnabled(listener.Name, enabled);
    }

#endregion

#region Operations

    /// <summary>
    /// Sets whether this web view instance should try to restore its view hierarchy when resumed.
    ///
    /// In some versions of Unity when running on Android, the player view is brought to front when switching back
    /// from a pause state, which causes the web view is invisible when the app is resumed. It requires an additional
    /// step to bring the web view to front to make the web view visible. Set this to true to apply this workaround.
    ///
    /// Issue caused by:
    /// https://issuetracker.unity3d.com/issues/android-a-black-screen-appears-for-a-few-seconds-when-returning-to-the-game-from-the-lock-screen-after-idle-time
    ///
    /// This issue is known in these released versions:
    /// - Unity 2021.3.31, 2021.3.32, 2021.3.31, 2021.3.34
    /// - Unity 2022.3.10, 2022.3.11, 2022.3.12, 2022.3.13, 2022.3.14, 2022.3.15
    ///
    /// If you are using UniWebView in these versions, you may want to set this value to `true`.
    /// </summary>
    public bool RestoreViewHierarchyOnResume { get; set; }

    /// <summary>
    /// Adds a JavaScript to current page.
    /// </summary>
    /// <param name="jsString">The JavaScript code to add. It should be a valid JavaScript statement string.</param>
    /// <param name="completionHandler">Called when adding JavaScript operation finishes. Default is `null`.</param>
    public void AddJavaScript(string jsString, Action<UniWebViewNativeResultPayload> completionHandler = null) {
        var identifier = Guid.NewGuid().ToString();
        UniWebViewInterface.AddJavaScript(listener.Name, jsString, identifier);
        if (completionHandler != null) {
            payloadActions.Add(identifier, completionHandler);
        }
    }

    /// <summary>
    /// Evaluates a JavaScript string on current page.
    /// </summary>
    /// <param name="jsString">The JavaScript string to evaluate.</param>
    /// <param name="completionHandler">Called when evaluating JavaScript operation finishes. Default is `null`.</param>
    public void EvaluateJavaScript(string jsString, Action<UniWebViewNativeResultPayload> completionHandler = null) {
        var identifier = Guid.NewGuid().ToString();
        UniWebViewInterface.EvaluateJavaScript(listener.Name, jsString, identifier);
        if (completionHandler != null) {
            payloadActions.Add(identifier, completionHandler);
        }
    }

    /// <summary>
    /// Cleans web view cache. This removes cached local data of web view. 
    /// 
    /// If you need to clear all cookies, use `ClearCookies` instead.
    /// </summary>
    /// <param name="includeStorage">
    /// Whether to include storage data (such as local database in the web page) when cleaning cache. Default `false`.
    /// </param>
    /// <param name="completionHandler">
    /// An action that is called after the operation finishes.
    /// </param>
    public void CleanCache(bool includeStorage = false, Action completionHandler = null) {
        var identifier = Guid.NewGuid().ToString();
        if (completionHandler != null) {
            actions.Add(identifier, completionHandler);
        }
        UniWebViewInterface.CleanCache(listener.Name, includeStorage, identifier);
    }

    /// <summary>
    /// Gets the HTML content from current page by accessing its outerHTML with JavaScript.
    /// </summary>
    /// <param name="handler">Called after the JavaScript executed. The parameter string is the content read 
    /// from page.</param>
    public void GetHTMLContent(Action<string> handler) {
        EvaluateJavaScript("document.documentElement.outerHTML", payload => {
            if (handler != null) {
                handler(payload.data);
            }
        });
    }


    /// <summary>
    /// Prints current page.
    /// 
    /// By calling this method, a native print preview panel will be brought up on iOS and Android. 
    /// This method does nothing on macOS editor.
    /// On iOS and Android, the web view does not support JavaScript (window.print()), 
    /// you can only initialize a print job from Unity by this method.
    /// </summary>
    public void Print() {
        UniWebViewInterface.Print(listener.Name);
    }
#endregion

#region Channel

    /// <summary>
    /// Registers a method handler for deciding whether UniWebView should handle the request received by the web view.
    ///
    /// The handler is called before the web view actually starts to load the new request. You can check the request
    /// properties, such as the URL, to decide whether UniWebView should continue to handle the request or not. If you
    /// return `true` from the handler function, UniWebView will continue to load the request. Otherwise, UniWebView
    /// will stop the loading.
    /// </summary>
    /// <param name="handler">
    /// A handler you can implement your own logic against the input request value. You need to return a boolean value
    /// to indicate whether UniWebView should continue to load the request or not as soon as possible.
    /// </param>
    public void RegisterShouldHandleRequest(Func<UniWebViewChannelMethodHandleRequest, bool> handler) {
        object Func(object obj) => handler((UniWebViewChannelMethodHandleRequest)obj);
        UniWebViewChannelMethodManager.Instance.RegisterChannelMethod(
            listener.Name, 
            UniWebViewChannelMethod.ShouldUniWebViewHandleRequest,
            Func
        );
    }
    
    /// <summary>
    /// Unregisters the method handler for handling request received by the web view.
    ///
    /// This clears the handler registered by `RegisterHandlingRequest` method.
    /// </summary>
    public void UnregisterShouldHandleRequest() {
        UniWebViewChannelMethodManager.Instance.UnregisterChannelMethod(
            listener.Name, 
            UniWebViewChannelMethod.ShouldUniWebViewHandleRequest
        );
    }

    /// <summary>
    /// Registers a method handler for deciding whether UniWebView should allow a media request from the web page or
    /// not.
    ///
    /// The handler is called when the web view receives a request to capture media, such as camera or microphone. It
    /// usually happens when the web view is trying to access the camera or microphone by using the "getUserMedia" APIs
    /// in WebRTC. You can check the request properties in the input `UniWebViewChannelMethodMediaCapturePermission`
    /// instance, which contains information like the media type, the request origin (protocol and host), then decide
    /// whether this media request should be allowed or not.
    ///
    /// According to the `UniWebViewMediaCapturePermissionDecision` value you return from the handler function,
    /// UniWebView behaves differently:
    ///  
    /// - `Grant`: UniWebView allows the access without asking the user.
    /// - `Deny`: UniWebView forbids the access and the web page will receive an error.
    /// - `Prompt`: UniWebView asks the user for permission. The web page will receive a prompt to ask the user if they
    /// allow the access to the requested media resources (camera or/and microphone).
    ///
    /// If this method is never called or the handler is unregistered, UniWebView will prompt the user for the
    /// permission.
    ///
    /// On iOS, this method is available from iOS 15.0 or later. On earlier version of iOS, the handler will be ignored
    /// and the web view will always prompt the user for the permission.
    /// 
    /// </summary>
    /// <param name="handler">
    /// A handler you can implement your own logic to decide whether UniWebView should allow, deny or prompt the media
    /// resource access request.
    ///
    /// You need to return a `UniWebViewMediaCapturePermissionDecision` value to indicate the decision as soon as
    /// possible.
    /// </param>
    public void RegisterOnRequestMediaCapturePermission(
        Func<
            UniWebViewChannelMethodMediaCapturePermission, 
            UniWebViewMediaCapturePermissionDecision
        > handler
        )
    {
        object Func(object obj) => handler((UniWebViewChannelMethodMediaCapturePermission)obj);
        UniWebViewChannelMethodManager.Instance.RegisterChannelMethod(
            listener.Name, 
            UniWebViewChannelMethod.RequestMediaCapturePermission,
            Func
        );
    }
    
    /// <summary>
    /// Unregisters the method handler for handling media capture permission request.
    ///
    /// This clears the handler registered by `RegisterOnRequestMediaCapturePermission` method.
    /// </summary>
    public void UnregisterOnRequestMediaCapturePermission() {
        UniWebViewChannelMethodManager.Instance.UnregisterChannelMethod(
            listener.Name, 
            UniWebViewChannelMethod.RequestMediaCapturePermission
        );
    }
    
    // Register channel message handler
    private void RegisterChannelMessageHandler() {
        object Func(object obj) => HandleChannelMessage((UniWebViewChannelMessage)obj);
        UniWebViewChannelMethodManager.Instance.RegisterChannelMethod(
            listener.Name,
            UniWebViewChannelMethod.ChannelMessage,
            Func
        );
    }
    
    // Handle incoming channel messages
    private UniWebViewChannelMessageResponse HandleChannelMessage(UniWebViewChannelMessage rawMessage) {
        
        // Create message with respond delegate for async responses
        var message = new UniWebViewChannelMessage(
            rawMessage.id,
            rawMessage.timestamp,
            rawMessage.action,
            rawMessage.data,
            rawMessage.messageType,
            SendChannelMessageResponseInternal
        );
        
        UniWebViewChannelMessageResponse response = null;

        // Log message type for debugging
        UniWebViewLogger.Instance.Debug(() => $"Processing {message.MessageType} message for action '{message.action}'");

        if (OnChannelMessageReceived != null) {
            try {
                response = OnChannelMessageReceived(this, message);

                // Validate response consistency with message type
                ValidateMessageResponse(message, response);
            } catch (System.Exception e) {
                UniWebViewLogger.Instance.Critical(() => $"Error in channel message handler: {e.Message}");
                response = UniWebViewChannelMessageResponse.Error("Handler execution failed: " + e.Message);
            }
        }

        return response;
    }

    // Validates that the response is appropriate for the message type
    private void ValidateMessageResponse(UniWebViewChannelMessage message, UniWebViewChannelMessageResponse response) {
        switch (message.MessageType) {
            case UniWebViewChannelMessageType.Send:
                if (response != null) {
                    UniWebViewLogger.Instance.Warning(() => $"Send message '{message.action}' returned a response, but Send messages should return null (fire-and-forget)");
                }
                break;

            case UniWebViewChannelMessageType.Call:
                if (response == null) {
                    UniWebViewLogger.Instance.Warning(() => $"Call message '{message.action}' returned null, but Call messages should return a response for immediate return to JavaScript");
                }
                break;

            case UniWebViewChannelMessageType.Request:
                // Request messages should always return null and use message.Respond() for async responses
                if (response != null) {
                    UniWebViewLogger.Instance.Warning(() => $"Request message '{message.action}' returned a response, but Request messages should return null and use message.Respond() for async responses");
                } else {
                    UniWebViewLogger.Instance.Debug(() => $"Request message '{message.action}' will respond later via message.Respond()");
                }
                break;
        }
    }

    // Internal method for async responses (called by UniWebViewChannelMessage.Respond)
    internal void SendChannelMessageResponseInternal(string messageId, UniWebViewChannelMessageResponse response) {
        try {
            // Escape the messageId to prevent XSS
            var escapedMessageId = messageId.Replace("'", "\\'").Replace("\"", "\\\"");

            string responseString;
            var errorFlag = response.hasError ? "true" : "false";

            if (response.hasError) {
                // For error responses, use JSON serialization (supports structured error objects)
                responseString = UniWebViewJsonUtility.ToJson(response.errorData);
            } else {
                // For success responses, use JSON serialization
                responseString = UniWebViewJsonUtility.ToJson(response.data);
            }

            var js = $"window.uniwebview._handleResponse('{escapedMessageId}', {responseString}, {errorFlag});";

            EvaluateJavaScript(js, (payload) => {
                if (payload.resultCode.Equals("0")) {
                    UniWebViewLogger.Instance.Debug(() => "Channel message response sent successfully");
                } else {
                    UniWebViewLogger.Instance.Critical(() => $"Failed to send channel message response: {payload.data}");
                }
            });
        } catch (System.Exception e) {
            UniWebViewLogger.Instance.Critical(() => $"Failed to send async response: {e.Message}");
        }
    }
#endregion

}
