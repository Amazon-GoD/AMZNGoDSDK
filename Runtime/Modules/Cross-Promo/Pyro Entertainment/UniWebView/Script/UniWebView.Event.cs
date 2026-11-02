//
//  UniWebView.Event.cs
//  Created by Wang Wei (@onevcat) on 2025-04-19.
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

using System;
using UnityEngine;

// UniWebView Event related part
public partial class UniWebView {
    /// <summary>
    /// Delegate for page started event.
    /// </summary>
    /// <param name="webView">The web view component which raises this event.</param>
    /// <param name="url">The url which the web view is about to load.</param>
    public delegate void PageStartedDelegate(UniWebView webView, string url);
    
    /// <summary>
    /// Raised when the web view starts loading a url.
    /// 
    /// This event will be invoked for both url loading with `Load` method or by a link navigating from page.
    /// </summary>
    public event PageStartedDelegate OnPageStarted;

    /// <summary>
    /// Delegate for page committed event.
    /// </summary>
    /// <param name="webView">The web view component which raises this event.</param>
    /// <param name="url">The url which the web view has started receiving content for.</param>
    public delegate void PageCommittedDelegate(UniWebView webView, string url);
    
    /// <summary>
    /// Raised when the web view receives response from the server and starts receiving web content.
    /// 
    /// This event will be invoked when the web view has confirmed the response is a web page and 
    /// has started to receive and process the web content. This happens after `OnPageStarted` but 
    /// before `OnPageFinished`.
    ///
    /// This is an ideal place to inject JavaScript code at the earliest possible moment when a page starts loading.
    /// Note that JavaScript execution is asynchronous - it may complete after the page finishes loading.
    /// For most cases, it is recommended to use `OnPageFinished` event instead, which ensures the page is fully loaded.
    /// </summary>
    public event PageCommittedDelegate OnPageCommitted;
    
    /// <summary>
    /// Delegate for page finished event.
    /// </summary>
    /// <param name="webView">The web view component which raises this event.</param>
    /// <param name="statusCode">HTTP status code received from response.</param>
    /// <param name="url">The url which the web view loaded.</param>
    public delegate void PageFinishedDelegate(UniWebView webView, int statusCode, string url);
    /// <summary>
    /// Raised when the web view finished to load a url successfully.
    /// 
    /// This method will be invoked when a valid response received from the url, regardless of the response status.
    /// If a url loading fails before reaching to the server and getting a response, `OnLoadingErrorReceived` will be 
    /// raised instead.
    /// </summary>
    public event PageFinishedDelegate OnPageFinished;

    /// <summary>
    /// Delegate for page loading error received event.
    /// </summary>
    /// <param name="webView">The web view component which raises this event.</param>
    /// <param name="errorCode">
    /// The error code which indicates the error type. 
    /// It can be different from systems and platforms.
    /// </param>
    /// <param name="errorMessage">The error message which indicates the error.</param>
    /// <param name="payload">The payload received from native side, which contains the error information, such as the failing URL, in its `Extra`.</param>
    public delegate void LoadingErrorReceivedDelegate(
        UniWebView webView, 
        int errorCode, 
        string errorMessage, 
        UniWebViewNativeResultPayload payload);

    /// <summary>
    /// Raised when an error encountered during the loading process. 
    /// Such as the "host not found" error or "no Internet connection" error will raise this event.
    /// 
    /// </summary>
    public event LoadingErrorReceivedDelegate OnLoadingErrorReceived;

    /// <summary>
    /// Delegate for page progress changed event.
    /// </summary>
    /// <param name="webView">The web view component which raises this event.</param>
    /// <param name="progress">A value indicates the loading progress of current page. It is a value between 0.0f and 1.0f.</param>
    public delegate void PageProgressChangedDelegate(UniWebView webView, float progress);
    /// <summary>
    /// Raised when the loading progress value changes in current web view.
    /// </summary>
    public event PageProgressChangedDelegate OnPageProgressChanged;

    /// <summary>
    /// Delegate for message received event.
    /// </summary>
    /// <param name="webView">The web view component which raises this event.</param>
    /// <param name="message">Message received from web view.</param>
    public delegate void MessageReceivedDelegate(UniWebView webView, UniWebViewMessage message);
    /// <summary>
    /// Raised when a message from web view is received. 
    /// 
    /// Generally, the message comes from a navigation to 
    /// a scheme which is observed by current web view. You could use `AddUrlScheme` and 
    /// `RemoveUrlScheme` to manipulate the scheme list.
    /// 
    /// "uniwebview://" scheme is default in the list, so a clicking on link starting with "uniwebview://"
    /// will raise this event, if it is not removed.
    /// </summary>
    public event MessageReceivedDelegate OnMessageReceived;

    /// <summary>
    /// Delegate for should close event.
    /// </summary>
    /// <param name="webView">The web view component which raises this event.</param>
    /// <returns>Whether the web view should be closed and destroyed.</returns>
    public delegate bool ShouldCloseDelegate(UniWebView webView);
    /// <summary>
    /// Raised when the web view is about to close itself.
    /// 
    /// This event is raised when the users close the web view by Back button on Android, Done button on iOS,
    /// or Close button on Unity Editor. It gives a chance to make final decision whether the web view should 
    /// be closed and destroyed. You can also clean all related resources you created (such as a reference to
    /// the web view) in this event.
    /// </summary>
    public event ShouldCloseDelegate OnShouldClose;

    /// <summary>
    /// Delegate for orientation changed event.
    /// </summary>
    /// <param name="webView">The web view component which raises this event.</param>
    /// <param name="orientation">The screen orientation for current state.</param>
    public delegate void OrientationChangedDelegate(UniWebView webView, ScreenOrientation orientation);
    /// <summary>
    /// Raised when the screen orientation is changed. It is a good time to set the web view frame if you 
    /// need to support multiple orientations in your game.
    /// </summary>
    public event OrientationChangedDelegate OnOrientationChanged;

    /// <summary>
    /// Delegate for content loading terminated event.
    /// </summary>
    /// <param name="webView">The web view component which raises this event.</param>
    public delegate void OnWebContentProcessTerminatedDelegate(UniWebView webView);
    /// <summary>
    /// On iOS, raise when the system calls `webViewWebContentProcessDidTerminate` method. On Android, raise when the
    /// system calls `onRenderProcessGone` method.
    /// 
    /// It is usually due to a low memory or the render process crashes when loading the web content. When this happens,
    /// the web view will leave you a blank white screen.
    /// 
    /// Usually you should close the web view and clean all the resource since there is no good way to restore. In some
    /// cases, you can also try to free as much as memory and do a page `Reload`.
    /// </summary>
    public event OnWebContentProcessTerminatedDelegate OnWebContentProcessTerminated;

    /// <summary>
    /// Delegate for file download task starting event.
    /// </summary>
    /// <param name="webView">The web view component which raises this event.</param>
    /// <param name="remoteUrl">The remote URL of this download task. This is also the download URL for the task.</param>
    /// <param name="fileName">The file name which user chooses to use.</param>
    public delegate void FileDownloadStarted(UniWebView webView, string remoteUrl, string fileName);
    /// <summary>
    /// Raised when a file download task starts.
    /// </summary>
    public event FileDownloadStarted OnFileDownloadStarted;

    /// <summary>
    /// Delegate for file download task finishing event.
    /// </summary>
    /// <param name="webView">The web view component which raises this event.</param>
    /// <param name="errorCode">
    /// The error code of the download task result. Value `0` means the download finishes without a problem. 
    /// Any other non-`0` value indicates an issue. The detail meaning of the error code depends on system. 
    /// On iOS, it is usually the `errorCode` of the received `NSURLError`. On Android, the value usually represents
    /// an `ERROR_*` value in `DownloadManager`.
    /// </param>
    /// <param name="remoteUrl">The remote URL of this download task.</param>
    /// <param name="diskPath">
    /// The file path of the downloaded file. On iOS, the downloader file is in a temporary folder of your app sandbox.
    /// On Android, it is in the "Download" folder of your app.
    /// </param>
    public delegate void FileDownloadFinished(UniWebView webView, int errorCode, string remoteUrl, string diskPath);
    /// <summary>
    /// Raised when a file download task finishes with either an error or success.
    /// </summary>
    public event FileDownloadFinished OnFileDownloadFinished;

    /// <summary>
    /// Delegate for capturing snapshot finished event.
    /// </summary>
    /// <param name="webView">The web view component which raises this event.</param>
    /// <param name="errorCode">
    /// The error code of the event. If the snapshot is captured and stored without a problem, the error code is 0.
    /// Any other number indicates an error happened. In most cases, the screenshot capturing only fails due to lack
    /// of disk storage.
    /// </param>
    /// <param name="diskPath">
    /// An accessible disk path to the captured snapshot image. If an error happens, it is an empty string.
    /// </param>
    public delegate void CaptureSnapshotFinished(UniWebView webView, int errorCode, string diskPath);
    /// <summary>
    /// Raised when an image captured and stored in a cache path on disk.
    /// </summary>
    public event CaptureSnapshotFinished OnCaptureSnapshotFinished;

    /// <summary>
    /// Delegate for multiple window opening event.
    /// </summary>
    /// <param name="webView">The web view component which opens the new multiple (pop-up) window.</param>
    /// <param name="multipleWindowId">The identifier of the opened new window.</param>
    public delegate void MultipleWindowOpenedDelegate(UniWebView webView, string multipleWindowId);
    /// <summary>
    /// Raised when a new window is opened. This happens when you enable the `SetSupportMultipleWindows` and open a
    /// new pop-up window.
    /// </summary>
    public event MultipleWindowOpenedDelegate OnMultipleWindowOpened;

    /// <summary>
    /// Delegate for multiple window closing event.
    /// </summary>
    /// <param name="webView">The web view component which closes the multiple window.</param>
    /// <param name="multipleWindowId">The identifier of the closed window.</param>
    public delegate void MultipleWindowClosedDelegate(UniWebView webView, string multipleWindowId);
    /// <summary>
    /// Raised when the multiple window is closed. This happens when the pop-up window is closed by navigation operation
    /// or by a invocation of `close()` on the page.
    /// </summary>
    public event MultipleWindowClosedDelegate OnMultipleWindowClosed;

    /// <summary>
    /// Delegate for channel message received event.
    /// </summary>
    /// <param name="webView">The web view component which raises this event.</param>
    /// <param name="message">The channel message received from JavaScript Bridge.</param>
    /// <returns>A response object that will be sent back to JavaScript. For sync calls, this is the immediate response. For async calls that don't need a response, this can be null.</returns>
    public delegate UniWebViewChannelMessageResponse ChannelMessageReceivedDelegate(UniWebView webView, UniWebViewChannelMessage message);
    /// <summary>
    /// Raised when a channel message from JavaScript Bridge is received.
    /// 
    /// This event is triggered when JavaScript code calls window.uniwebview.send(),
    /// window.uniwebview.call(), or window.uniwebview.request(). The handler should
    /// return a UniWebViewChannelMessageResponse for sync calls or when immediate response
    /// is needed. For async messages that need delayed response, use message.Respond() method.
    /// </summary>
    public event ChannelMessageReceivedDelegate OnChannelMessageReceived;
    

    /* //////////////////////////////////////////////////////
    // Internal Listener Interface
    ////////////////////////////////////////////////////// */
    internal void InternalOnPageStarted(string url) {
        if (OnPageStarted != null) {
            OnPageStarted(this, url);
        }
    }

    internal void InternalOnShowTransitionFinished(string identifier) {
        Action action;
        if (actions.TryGetValue(identifier, out action)) {
            action();
            actions.Remove(identifier);
        }
    }

    internal void InternalOnHideTransitionFinished(string identifier) {
        Action action;
        if (actions.TryGetValue(identifier, out action)) {
            action();
            actions.Remove(identifier);
        }
    }

    internal void InternalOnAnimateToFinished(string identifier) {
        Action action;
        if (actions.TryGetValue(identifier, out action)) {
            action();
            actions.Remove(identifier);
        }
    }

    internal void InternalOnGeneralCallback(string identifier) {
        Action action;
        if (actions.TryGetValue(identifier, out action)) {
            action();
            actions.Remove(identifier);
        }
    }

    internal void InternalOnAddJavaScriptFinished(UniWebViewNativeResultPayload payload) {
        Action<UniWebViewNativeResultPayload> action;
        var identifier = payload.identifier;
        if (payloadActions.TryGetValue(identifier, out action)) {
            action(payload);
            payloadActions.Remove(identifier);
        }
    }

    internal void InternalOnEvalJavaScriptFinished(UniWebViewNativeResultPayload payload) {
        Action<UniWebViewNativeResultPayload> action;
        var identifier = payload.identifier;
        if (payloadActions.TryGetValue(identifier, out action)) {
            action(payload);
            payloadActions.Remove(identifier);
        }
    }

    internal void InternalOnPageFinished(UniWebViewNativeResultPayload payload) {
        if (OnPageFinished != null) {
            int code = -1;
            if (int.TryParse(payload.resultCode, out code)) {
                OnPageFinished(this, code, payload.data);
            } else {
                UniWebViewLogger.Instance.Critical("Invalid status code received: " + payload.resultCode);
            }
        }
    }

    internal void InternalOnPageCommitted(string url) {
        if (OnPageCommitted != null) {
            OnPageCommitted(this, url);
        }
    }

    internal void InternalOnPageErrorReceived(UniWebViewNativeResultPayload payload) {
        if (OnLoadingErrorReceived != null) {
            if (int.TryParse(payload.resultCode, out var code)) {
                OnLoadingErrorReceived(this, code, payload.data, payload);
            } else {
                UniWebViewLogger.Instance.Critical("Invalid error code received: " + payload.resultCode);
            }
        } else if (OnPageErrorReceived != null) {
            if (int.TryParse(payload.resultCode, out var code)) {
                OnPageErrorReceived(this, code, payload.data);
            } else {
                UniWebViewLogger.Instance.Critical("Invalid error code received: " + payload.resultCode);
            }
        }
    }

    internal void InternalOnPageProgressChanged(float progress) {
        if (OnPageProgressChanged != null) {
            OnPageProgressChanged(this, progress);
        }
    }

    internal void InternalOnMessageReceived(string result) {
         if (OnMessageReceived != null) {
             var message = new UniWebViewMessage(result);
             OnMessageReceived(this, message);
         }
    }

    internal void InternalOnShouldClose() {
        if (OnShouldClose != null) {
            var shouldClose = OnShouldClose(this);
            if (shouldClose) {
                Destroy(this);
            }
        } else {
            Destroy(this);
        }
    }

    internal void InternalOnWebContentProcessDidTerminate() {
        if (OnWebContentProcessTerminated != null) {
            OnWebContentProcessTerminated(this);
        }
    }

    internal void InternalOnMultipleWindowOpened(string multiWindowId) {
        if (OnMultipleWindowOpened != null) {
            OnMultipleWindowOpened(this, multiWindowId);
        }
    }

    internal void InternalOnMultipleWindowClosed(string multiWindowId) {
        if (OnMultipleWindowClosed != null) {
            OnMultipleWindowClosed(this, multiWindowId);
        }
    }

    internal void InternalOnFileDownloadStarted(UniWebViewNativeResultPayload payload) {
        if (OnFileDownloadStarted != null) {
            OnFileDownloadStarted(this, payload.identifier, payload.data);
        }
    }

    internal void InternalOnFileDownloadFinished(UniWebViewNativeResultPayload payload) {
        if (OnFileDownloadFinished != null) {
            int errorCode = int.TryParse(payload.resultCode, out errorCode) ? errorCode : -1;
            OnFileDownloadFinished(this, errorCode, payload.identifier, payload.data);
        }
    }

    internal void InternalOnCaptureSnapshotFinished(UniWebViewNativeResultPayload payload) {
        if (OnCaptureSnapshotFinished != null) {
            int errorCode = int.TryParse(payload.resultCode, out errorCode) ? errorCode : -1;
            OnCaptureSnapshotFinished(this, errorCode,  payload.data);
        }
    }
    
    internal void InternalOnSnapshotRenderingStarted(string identifier) {
        if (!actions.TryGetValue(identifier, out var action)) {
            return;
        }
        action();
        actions.Remove(identifier);
    }

    internal static void InternalCookieOperation(UniWebViewNativeResultPayload payload) {
        var identifier = payload.identifier;
        if (!globalPayloadActions.TryGetValue(identifier, out var action)) {
            return;
        }
        action(payload);
        globalPayloadActions.Remove(identifier);
    }
}