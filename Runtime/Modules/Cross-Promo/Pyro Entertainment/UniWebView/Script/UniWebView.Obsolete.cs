//
//  UniWebView.Obsolete.cs
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

// UniWebView Obsolete related part
public partial class UniWebView {

    [Obsolete("Use Toolbar is deprecated. Use the embedded toolbar instead.", false)]
    [SerializeField][HideInInspector]
    private bool useToolbar;

    [Obsolete("Use Toolbar is deprecated. Use the embedded toolbar instead.", false)]
    [SerializeField][HideInInspector]
    private UniWebViewToolbarPosition toolbarPosition;

    /// <summary>
    /// Delegate for page error received event.
    ///
    /// Deprecated. Use `LoadingErrorReceivedDelegate` instead.
    /// </summary>
    /// <param name="webView">The web view component which raises this event.</param>
    /// <param name="errorCode">
    /// The error code which indicates the error type. 
    /// It can be different from systems and platforms.
    /// </param>
    /// <param name="errorMessage">The error message which indicates the error.</param>
    [Obsolete("PageErrorReceivedDelegate is deprecated. Use `LoadingErrorReceivedDelegate` instead.", false)]
    public delegate void PageErrorReceivedDelegate(UniWebView webView, int errorCode, string errorMessage);
    
    /// <summary>
    /// Raised when an error encountered during the loading process. 
    /// Such as the "host not found" error or "no Internet connection" error will raise this event.
    ///
    /// Deprecated. Use `OnLoadingErrorReceived` instead. If both `OnPageErrorReceived` and `OnLoadingErrorReceived` are
    /// listened, only the new `OnLoadingErrorReceived` will be raised, `OnPageErrorReceived` will not be called.
    /// </summary>
    [Obsolete("OnPageErrorReceived is deprecated. Use `OnLoadingErrorReceived` instead.", false)]
    public event PageErrorReceivedDelegate OnPageErrorReceived;

    /// <summary>
    /// Sets whether the web view should behave in immersive mode, that is, 
    /// hides the status bar and navigation bar with a sticky style.
    /// 
    /// This method is only for Android. Default is enabled.
    /// </summary>
    /// <param name="enabled"></param>
    [Obsolete("SetImmersiveModeEnabled is deprecated. Now UniWebView always respect navigation bar/status bar settings from Unity.", false)]
    public void SetImmersiveModeEnabled(bool enabled) {
        Debug.LogError(
            "SetImmersiveModeEnabled is removed in UniWebView 4." + 
            "Now UniWebView always respect navigation bar/status bar settings from Unity."
        );
    } 
    /// <summary>
    /// Delegate for code keycode received event.
    /// </summary>
    /// <param name="webView">The web view component which raises this event.</param>
    /// <param name="keyCode">The key code of pressed key. See [Android API for keycode](https://developer.android.com/reference/android/view/KeyEvent.html#KEYCODE_0) to know the possible values.</param>
    [Obsolete("KeyCodeReceivedDelegate is deprecated. Now UniWebView never intercepts device key code events. Check `Input.GetKeyUp` instead.", false)]
    public delegate void KeyCodeReceivedDelegate(UniWebView webView, int keyCode);

    /// <summary>
    /// Raised when a key (like back button or volume up) on the device is pressed.
    /// 
    /// This event only raised on Android. It is useful when you disabled the back button but still need to 
    /// get the back button event. On iOS, user's key action is not available and this event will never be 
    /// raised.
    /// </summary>
    [Obsolete("OnKeyCodeReceived is deprecated and never called. Now UniWebView never intercepts device key code events. Check `Input.GetKeyUp` instead.", false)]
#pragma warning disable CS0067
    public event KeyCodeReceivedDelegate OnKeyCodeReceived;

    /// <summary>
    /// Adds a trusted domain to white list and allow permission requests from the domain.
    /// 
    /// You need this on Android devices when a site needs the location or camera  permission. It will allow the
    /// permission gets approved so you could access the corresponding devices.
    ///
    /// Deprecated. Use `RegisterOnRequestMediaCapturePermission` instead, which works for both Android and iOS and
    /// provides a more flexible way to handle the permission requests. By default, if neither this method and
    /// `RegisterOnRequestMediaCapturePermission` is called, the permission request will trigger a grant alert to ask
    /// the user to decide whether to allow or deny the permission.
    ///  
    /// </summary>
    /// <param name="domain">The domain to add to the white list.</param>
    [Obsolete("Deprecated. Use `RegisterOnRequestMediaCapturePermission` instead. Check " + 
              "https://docs.uniwebview.com/api/#registeronrequestmediacapturepermission", false)]
    public void AddPermissionTrustDomain(string domain) {
        #if UNITY_ANDROID && !UNITY_EDITOR
        UniWebViewInterface.AddPermissionTrustDomain(listener.Name, domain);
        #endif
    }

    /// <summary>
    /// Removes a trusted domain from white list.
    /// </summary>
    /// <param name="domain">The domain to remove from white list.</param>
    [Obsolete("Deprecated. Use `UnregisterOnRequestMediaCapturePermission` instead.", false)]
    public void RemovePermissionTrustDomain(string domain) {
        #if UNITY_ANDROID && !UNITY_EDITOR
        UniWebViewInterface.RemovePermissionTrustDomain(listener.Name, domain);
        #endif
    }

    /// <summary>
    /// Sets whether to show a toolbar which contains navigation buttons and Done button.
    /// 
    /// You could choose to show or hide the tool bar. By configuring the `animated` and `onTop` 
    /// parameters, you can control the animating and position of the toolbar. If the toolbar is 
    /// overlapping with some part of your web view, pass `adjustInset` with `true` to have the 
    /// web view relocating itself to avoid the overlap.
    /// 
    /// This method is only for iOS. The toolbar is hidden by default.
    /// </summary>
    /// <param name="show">Whether the toolbar should show or hide.</param>
    /// <param name="animated">Whether the toolbar state changing should be with animation. Default is `false`.</param>
    /// <param name="onTop">Whether the toolbar should snap to top of screen or to bottom of screen. 
    /// Default is `true`</param>
    /// <param name="adjustInset">Whether the toolbar transition should also adjust web view position and size
    ///  if overlapped. Default is `false`</param>
    [Obsolete("`SetShowToolbar` is deprecated. Use `EmbeddedToolbar.Show()` or `EmbeddedToolbar.Hide()`" + 
              "instead.", false)]
    public void SetShowToolbar(bool show, bool animated = false, bool onTop = true, bool adjustInset = false) {
        #if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR_WIN && !UNITY_EDITOR_LINUX
        UniWebViewInterface.SetShowToolbar(listener.Name, show, animated, onTop, adjustInset);
        #endif
    }

    /// <summary>
    /// Sets the done button text in toolbar.
    /// 
    /// By default, UniWebView will show a "Done" button at right size in the 
    /// toolbar. You could change its title by passing a text.
    /// 
    /// This method is only for iOS, since there is no toolbar on Android.
    /// </summary>
    /// <param name="text">The text needed to be set as done button title.</param>
    [Obsolete("`SetToolbarDoneButtonText` is deprecated. Use `EmbeddedToolbar.SetDoneButtonText` " + 
              "instead.", false)]
    public void SetToolbarDoneButtonText(string text) {
        #if UNITY_IOS && !UNITY_EDITOR
        UniWebViewInterface.SetToolbarDoneButtonText(listener.Name, text);
        #endif
    }

    /// <summary>
    /// Sets the go back button text in toolbar.
    /// 
    /// By default, UniWebView will show a back arrow at the left side in the 
    /// toolbar. You could change its text.
    /// 
    /// This method is only for iOS and macOS Editor, since there is no toolbar on Android.
    /// </summary>
    /// <param name="text">The text needed to be set as go back button.</param>
    [Obsolete("`SetToolbarGoBackButtonText` is deprecated. Use `EmbeddedToolbar.SetGoBackButtonText` " + 
              "instead.", false)]
    public void SetToolbarGoBackButtonText(string text) {
        #if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR_WIN && !UNITY_EDITOR_LINUX
        UniWebViewInterface.SetToolbarGoBackButtonText(listener.Name, text);
        #endif
    }

        /// <summary>
    /// Sets the go forward button text in toolbar.
    /// 
    /// By default, UniWebView will show a forward arrow at the left side in the 
    /// toolbar. You could change its text.
    /// 
    /// This method is only for iOS and macOS Editor, since there is no toolbar on Android.
    /// </summary>
    /// <param name="text">The text needed to be set as go forward button.</param>
    [Obsolete("`SetToolbarGoForwardButtonText` is deprecated. Use `EmbeddedToolbar.SetGoForwardButtonText` " + 
              "instead.", false)]
    public void SetToolbarGoForwardButtonText(string text) {
        #if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR_WIN && !UNITY_EDITOR_LINUX
        UniWebViewInterface.SetToolbarGoForwardButtonText(listener.Name, text);
        #endif
    }

    /// <summary>
    /// Sets the background tint color for the toolbar.
    /// 
    /// By default, UniWebView uses a default half-transparent iOS standard background for toolbar.
    /// You can change it by setting a new opaque color.
    /// 
    /// This method is only for iOS, since there is no toolbar on Android.
    /// </summary>
    /// <param name="color">The color should be used for the background tint of the toolbar.</param>
    [Obsolete("`SetToolbarTintColor` is deprecated. Use `EmbeddedToolbar.SetBackgroundColor` " + 
              "instead.", false)]
    public void SetToolbarTintColor(Color color) {
        #if UNITY_IOS && !UNITY_EDITOR
        UniWebViewInterface.SetToolbarTintColor(listener.Name, color.r, color.g, color.b);
        #endif
    }

    /// <summary>
    /// Sets the button text color for the toolbar.
    /// 
    /// By default, UniWebView uses the default text color on iOS, which is blue for most cases.
    /// You can change it by setting a new opaque color.
    /// 
    /// This method is only for iOS, since there is no toolbar on Android.
    /// </summary>
    /// <param name="color">The color should be used for the button text of the toolbar.</param>
    [Obsolete("`SetToolbarTextColor` is deprecated. Use `EmbeddedToolbar.SetButtonTextColor` or " + 
              "`EmbeddedToolbar.SetTitleTextColor` instead.", false)]
    public void SetToolbarTextColor(Color color) {
        #if UNITY_IOS && !UNITY_EDITOR
        UniWebViewInterface.SetToolbarTextColor(listener.Name, color.r, color.g, color.b);
        #endif
    }

    /// <summary>
    /// Sets the visibility of navigation buttons, such as "Go Back" and "Go Forward", on toolbar.
    /// 
    /// By default, UniWebView will show the "Go Back" and "Go Forward" navigation buttons on the toolbar.
    /// Users can use these buttons to perform go back or go forward action just like in a browser. If the navigation
    /// model is not for your case, call this method with `false` as `show` parameter to hide them.
    /// 
    /// This method is only for iOS, since there is no toolbar on Android.
    /// </summary>
    /// <param name="show">Whether the navigation buttons on the toolbar should show or hide.</param>
    [Obsolete("`SetShowToolbarNavigationButtons` is deprecated. Use `EmbeddedToolbar.ShowNavigationButtons` or " + 
              "`EmbeddedToolbar.HideNavigationButtons` instead.", false)]
    public void SetShowToolbarNavigationButtons(bool show) {
        #if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR_WIN && !UNITY_EDITOR_LINUX
        UniWebViewInterface.SetShowToolbarNavigationButtons(listener.Name, show);
        #endif
    }

    [ObsoleteAttribute("Deprecated. Use `SetSupportMultipleWindows(bool enabled, bool allowJavaScriptOpen)` to set `allowJavaScriptOpen` explicitly.")]
    public void SetSupportMultipleWindows(bool enabled) {
        SetSupportMultipleWindows(enabled, true);
    }

    /// <summary>
    /// Clears all cookies from web view.
    /// 
    /// This will clear cookies from all domains in the web view and previous.
    /// If you only need to remove cookies from a certain domain, use `RemoveCookies` instead.
    ///
    /// Deprecated. Use the async version `ClearCookies(Action)` with action instead.
    /// </summary>
    [Obsolete("Use the async version `ClearCookies(Action)` instead.")]
    public static void ClearCookies() {
        UniWebViewInterface.ClearCookies();
    }

    /// <summary>
    /// Sets a cookie for a certain url.
    ///
    /// Deprecated. Use the async version `SetCookie(String,String,Action)` with action instead.
    /// </summary>
    /// <param name="url">The url to which cookie will be set.</param>
    /// <param name="cookie">The cookie string to set.</param>
    /// <param name="skipEncoding">
    /// Whether UniWebView should skip encoding the url or not. If set to `false`, UniWebView will try to encode the url parameter before
    /// using it. Otherwise, your original url string will be used to set the cookie if it is valid. Default is `false`.
    /// </param>
    [Obsolete("Use the async version `SetCookie(String,String,Action)` instead.")]
    public static void SetCookie(string url, string cookie, bool skipEncoding = false) {
        UniWebViewInterface.SetCookie(url, cookie, skipEncoding);
    }

    /// <summary>
    /// Gets the cookie value under a given url and key.
    ///
    /// Deprecated. Use the async version `GetCookie(String,String,Action)` with action instead.
    /// </summary>
    /// <param name="url">The url (domain) where the target cookie is.</param>
    /// <param name="key">The key for target cookie value.</param>
    /// <param name="skipEncoding">
    /// Whether UniWebView should skip encoding the url or not. If set to `false`, UniWebView will try to encode the url parameter before
    /// using it. Otherwise, your original url string will be used to get the cookie if it is valid. Default is `false`.
    /// </param>
    /// <returns>Value of the target cookie under url.</returns>
    [Obsolete("Use the async version `GetCookie(String,String,Action)` instead.")]
    public static string GetCookie(string url, string key, bool skipEncoding = false) {
        return UniWebViewInterface.GetCookie(url, key, skipEncoding);
    }
    
    /// <summary>
    /// Gets the cookie value under a url and key. When it finishes, the `handler` will be called with the cookie value
    /// if exists.
    /// </summary>
    /// <param name="url">The url (domain) where the target cookie is.</param>
    /// <param name="key">The key for target cookie value.</param>
    /// <param name="handler">An action that is called after the operation finishes.</param>
    public static void GetCookie(string url, string key, Action<string> handler) {
        GetCookie(url, key, false, handler);
    }

    /// <summary>
    /// Removes all the cookies under a given url.
    /// </summary>
    /// <param name="url">The url (domain) where the cookies are under.</param>
    /// <param name="skipEncoding">
    /// Whether UniWebView should skip encoding the url or not. If set to `false`, UniWebView will try to encode the url parameter before
    /// using it. Otherwise, your original url string will be used to get the cookie if it is valid. Default is `false`.
    /// </param>
    [Obsolete("Use the async version `RemoveCookies(String,Action)` instead.")]
    public static void RemoveCookies(string url, bool skipEncoding = false) {
        UniWebViewInterface.RemoveCookies(url, skipEncoding);
    }

    /// <summary>
    /// Removes a certain cookie under the given url for the specified key.
    ///
    /// Deprecated. Use the async version `RemoveCookie(String,String,Action)` with action instead.
    /// </summary>
    /// <param name="url">The url (domain) where the cookies are under.</param>
    /// <param name="key">The key for target cookie.</param>
    /// <param name="skipEncoding">
    /// Whether UniWebView should skip encoding the url or not. If set to `false`, UniWebView will try to encode the url parameter before
    /// using it. Otherwise, your original url string will be used to get the cookie if it is valid. Default is `false`.
    /// </param>
    [Obsolete("Use the async version `RemoveCookie(String,String,Action)` instead.")]
    public static void RemoveCooke(string url, string key, bool skipEncoding = false) {
        RemoveCookie(url, key, skipEncoding);
    }

    /// <summary>
    /// Removes a certain cookie under the given url for the specified key.
    ///
    /// Deprecated. Use the async version `RemoveCookie(String,String,Action)` with action instead.
    /// </summary>
    /// <param name="url">The url (domain) where the cookies is under.</param>
    /// <param name="key">The key for target cookie.</param>
    /// <param name="skipEncoding">
    /// Whether UniWebView should skip encoding the url or not. If set to `false`, UniWebView will try to encode the url parameter before
    /// using it. Otherwise, your original url string will be used to get the cookie if it is valid. Default is `false`.
    /// </param>
    [Obsolete("Use the async version `RemoveCookie(String,String,Action)` instead.")]
    public static void RemoveCookie(string url, string key, bool skipEncoding = false) {
        UniWebViewInterface.RemoveCookie(url, key, skipEncoding);
    }
}