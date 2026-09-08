//
//  UniWebView.Networking.cs
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

public partial class UniWebView {
    /// <summary>
    /// Adds a url scheme to UniWebView message system interpreter.
    /// All following url navigation to this scheme will be sent as a message to UniWebView instead.
    /// </summary>
    /// <param name="scheme">The url scheme to add. It should not contain "://" part. You could even add "http" and/or 
    /// "https" to prevent all resource loading on the page. "uniwebview" is added by default. Nothing will happen if 
    /// you try to add a duplicated scheme.</param>
    public void AddUrlScheme(string scheme) {
        if (scheme == null) {
            UniWebViewLogger.Instance.Critical("The scheme should not be null.");
            return;
        }

        if (scheme.Contains("://")) {
            UniWebViewLogger.Instance.Critical("The scheme should not include invalid characters '://'");
            return;
        }
        UniWebViewInterface.AddUrlScheme(listener.Name, scheme);
    }

    /// <summary>
    /// Removes a url scheme from UniWebView message system interpreter.
    /// </summary>
    /// <param name="scheme">The url scheme to remove. Nothing will happen if the scheme is not in the message system.</param>
    public void RemoveUrlScheme(string scheme) {
        if (scheme == null) {
            UniWebViewLogger.Instance.Critical("The scheme should not be null.");
            return;
        }
        if (scheme.Contains("://")) {
            UniWebViewLogger.Instance.Critical("The scheme should not include invalid characters '://'");
            return;
        }
        UniWebViewInterface.RemoveUrlScheme(listener.Name, scheme);
    }


    /// <summary>
    /// Adds a domain to the SSL checking white list.
    /// If you are trying to access a web site with untrusted or expired certification, 
    /// the web view will prevent its loading. If you could confirm that this site is trusted,
    /// you can add the domain as an SSL exception, so you could visit it.
    /// </summary>
    /// <param name="domain">The domain to add. It should not contain any scheme or path part in url.</param>
    public void AddSslExceptionDomain(string domain) {
        if (domain == null) {
            UniWebViewLogger.Instance.Critical("The domain should not be null.");
            return;
        }
        if (domain.Contains("://")) {
            UniWebViewLogger.Instance.Critical("The domain should not include invalid characters '://'");
            return;
        }
        UniWebViewInterface.AddSslExceptionDomain(listener.Name, domain);
    }

    /// <summary>
    /// Removes a domain from the SSL checking white list.
    /// </summary>
    /// <param name="domain">The domain to remove. It should not contain any scheme or path part in url.</param>
    public void RemoveSslExceptionDomain(string domain) {
        if (domain == null) {
            UniWebViewLogger.Instance.Critical("The domain should not be null.");
            return;
        }
        if (domain.Contains("://")) {
            UniWebViewLogger.Instance.Critical("The domain should not include invalid characters '://'");
            return;
        }
        UniWebViewInterface.RemoveSslExceptionDomain(listener.Name, domain);
    }

    /// <summary>
    /// Sets a customized header field for web view requests.
    /// 
    /// The header field will be used for all subsequence request. 
    /// Pass `null` as value to unset a header field.
    /// 
    /// Some reserved headers like user agent are not be able to override by setting here, 
    /// use the `SetUserAgent` method for them instead.
    /// </summary>
    /// <param name="key">The key of customized header field.</param>
    /// <param name="value">The value of customized header field. `null` if you want to unset the field.</param>
    public void SetHeaderField(string key, string value) {
        if (key == null) {
            UniWebViewLogger.Instance.Critical("Header key should not be null.");
            return;
        }
        UniWebViewInterface.SetHeaderField(listener.Name, key, value);
    }

    /// <summary>
    /// Sets the user agent used in the web view. 
    /// If the string is null or empty, the system default value will be used. 
    /// </summary>
    /// <param name="agent">The new user agent string to use.</param>
    public void SetUserAgent(string agent) {
        UniWebViewInterface.SetUserAgent(listener.Name, agent);
    }

    /// <summary>
    /// Gets the user agent string currently used in web view.
    /// If a customized user agent is not set, the default user agent in current platform will be returned.
    /// </summary>
    /// <returns>The user agent string in use.</returns>
    public string GetUserAgent() {
        return UniWebViewInterface.GetUserAgent(listener.Name);
    }

    /// <summary>
    /// Sets whether loading a local file is allowed.
    /// 
    /// If set to `false`, any load from a file URL `file://` for `Load` method will be rejected and trigger an 
    /// `OnLoadingErrorReceived` event. That means you cannot load a web page from any local file. If you do not going to 
    /// load any local files, setting it to `false` helps to reduce the interface of web view and improve the security.
    /// 
    /// By default, it is `true` and the local file URL loading is allowed.
    /// </summary>
    /// <param name="flag">Whether the local file access by web view loading is allowed or not.</param>
    public void SetAllowFileAccess(bool flag) {
        UniWebViewInterface.SetAllowFileAccess(listener.Name, flag);
    }

    /// <summary>
    /// Sets whether file access from file URLs is allowed.
    /// 
    /// By setting with `true`, access to file URLs inside the web view will be enabled and you could access 
    /// sub-resources or make cross origin requests from local HTML files.
    /// 
    /// On iOS, it uses some "hidden" way by setting `allowFileAccessFromFileURLs` in config preferences for WebKit.
    /// So it is possible that it stops working in a future version.
    /// 
    /// On Android, it sets the `WebSettings.setAllowFileAccessFromFileURLs` for the current web view.
    /// </summary>
    /// <param name="flag">Whether the file access inside web view from file URLs is allowed or not.</param>
    public void SetAllowFileAccessFromFileURLs(bool flag) {
        UniWebViewInterface.SetAllowFileAccessFromFileURLs(listener.Name, flag);
    }

    /// <summary>
    /// Sets allow universal access from file URLs. By default, on iOS, the `WKWebView` forbids any load of local files
    /// through AJAX even when opening a local HTML file. It checks the CORS rules and fails at web view level. 
    /// This is useful when you want access these files by setting the `allowUniversalAccessFromFileURLs` key of web view
    /// configuration.
    /// 
    /// On iOS and macOS Editor. It uses some "hidden" way by setting `allowUniversalAccessFromFileURLs` in config 
    /// for WebKit. So it is possible that it stops working in a future version.
    /// 
    /// On Android, it sets the `WebSettings.setAllowUniversalAccessFromFileURLs` and any later-created web views uses
    /// that value.
    /// </summary>
    /// <param name="flag">A flag indicates whether the universal access for files are allowed or not.</param>
    public static void SetAllowUniversalAccessFromFileURLs(bool flag) {
        UniWebViewInterface.SetAllowUniversalAccessFromFileURLs(flag);
    }


    /// <summary>
    /// Sets whether the web view limits navigation to pages within the app’s domain.
    ///
    /// This only works on iOS 14.0+. For more information, refer to the Apple's documentation:
    /// https://developer.apple.com/documentation/webkit/wkwebviewconfiguration/3585117-limitsnavigationstoappbounddomai
    /// and the App-Bound Domains page: https://webkit.org/blog/10882/app-bound-domains/
    ///
    /// This requires additional setup in `WKAppBoundDomains` key in the Info.plist file.
    ///
    /// On Android, this method does nothing.
    /// </summary>
    /// <param name="enabled"></param>
    public static void SetLimitsNavigationsToAppBoundDomains(bool enabled) {
        #if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR_WIN && !UNITY_EDITOR_LINUX
        UniWebViewInterface.SetLimitsNavigationsToAppBoundDomains(enabled);
        #endif
    }

    /// <summary>
    /// Clears any saved credentials for HTTP authentication for both Basic and Digest.
    /// 
    /// On both iOS and Android, the user input credentials will be stored permanently across session.
    /// It could prevent your users to input username and password again until they changed. If you need the 
    /// credentials only living in a shorter lifetime, call this method at proper timing.
    /// 
    /// On iOS, it will clear the credentials immediately and completely from both disk and network cache. 
    /// On Android, it only clears from disk database, the authentication might be still cached in the network stack
    /// and will not be removed until next session (app restarting). 
    /// 
    /// The client logout mechanism should be implemented by the Web site designer (such as server sending a HTTP 
    /// 401 for invalidating credentials).
    /// 
    /// </summary>
    /// <param name="host">The host to which the credentials apply. It should not contain any thing like scheme or path part.</param>
    /// <param name="realm">The realm to which the credentials apply.</param>
    public static void ClearHttpAuthUsernamePassword(string host, string realm) {
        UniWebViewInterface.ClearHttpAuthUsernamePassword(host, realm);
    }

    /// <summary>
    /// Sets whether a prompt alert should be displayed for collection username and password when the web view receives an
    /// HTTP authentication challenge (HTTP Basic or HTTP Digest) from server.
    /// 
    /// By setting with `false`, no prompt will be shown and the user cannot login with input credentials. In this case,
    /// you can only access this page by providing username and password through the URL like: "http://username:password@example.com".
    /// If the username and password does not match, normally an error with 401 as status code would be returned (this behavior depends
    /// on the server implementation). If set with `true`, a prompt will be shown when there is no credentials provided or it is not
    /// correct in the URL.
    /// 
    /// Default is `true`.
    /// </summary>
    /// <param name="flag">Whether a prompt alert should be shown for HTTP authentication challenge or not.</param>
    public void SetAllowHTTPAuthPopUpWindow(bool flag) {
        UniWebViewInterface.SetAllowHTTPAuthPopUpWindow(listener.Name, flag);
    }

    /// <summary>
    /// Adds the URL to download inspecting list.
    /// 
    /// If a response is received in main frame and its URL is already in the inspecting list, a download task will be 
    /// triggered. Check "Download Files" guide for more.
    /// 
    /// This method only works on iOS and macOS Editor.
    /// </summary>
    /// <param name="urlString">The inspected URL.</param>
    /// <param name="type">The download matching type used to match the URL. Default is `ExactValue`.</param>
    public void AddDownloadURL(string urlString, UniWebViewDownloadMatchingType type = UniWebViewDownloadMatchingType.ExactValue) {
        #if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR_WIN && !UNITY_EDITOR_LINUX
        UniWebViewInterface.AddDownloadURL(listener.Name, urlString, (int)type);
        #endif
    }

    /// <summary>
    /// Removes the URL from download inspecting list.
    /// 
    /// If a response is received in main frame and its URL is already in the inspecting list, a download task will be 
    /// triggered. Check "Download Files" guide for more.
    /// 
    /// This method only works on iOS and macOS Editor.
    /// </summary>
    /// <param name="urlString">The inspected URL.</param>
    /// <param name="type">The download matching type used to match the URL. Default is `ExactValue`.</param>
    /// 
    public void RemoveDownloadURL(string urlString, UniWebViewDownloadMatchingType type = UniWebViewDownloadMatchingType.ExactValue) {
        #if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR_WIN && !UNITY_EDITOR_LINUX
        UniWebViewInterface.RemoveDownloadURL(listener.Name, urlString, (int)type);
        #endif
    }

    /// <summary>
    /// Adds the MIME type to download inspecting list.
    /// 
    /// If a response is received in main frame and its MIME type is already in the inspecting list, a 
    /// download task will be triggered. Check "Download Files" guide for more.
    /// 
    /// This method only works on iOS and macOS Editor.
    /// </summary>
    /// <param name="MIMEType">The inspected MIME type of the response.</param>
    /// <param name="type">The download matching type used to match the MIME type. Default is `ExactValue`.</param>
    public void AddDownloadMIMEType(string MIMEType, UniWebViewDownloadMatchingType type = UniWebViewDownloadMatchingType.ExactValue) {
        #if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR_WIN && !UNITY_EDITOR_LINUX
        UniWebViewInterface.AddDownloadMIMEType(listener.Name, MIMEType, (int)type);
        #endif
    }

    /// <summary>
    /// Removes the MIME type from download inspecting list.
    /// 
    /// If a response is received in main frame and its MIME type is already in the inspecting list, a 
    /// download task will be triggered. Check "Download Files" guide for more.
    /// 
    /// This method only works on iOS and macOS Editor.
    /// </summary>
    /// <param name="MIMEType">The inspected MIME type of the response.</param>
    /// <param name="type">The download matching type used to match the MIME type. Default is `ExactValue`.</param>
    public void RemoveDownloadMIMETypes(string MIMEType, UniWebViewDownloadMatchingType type = UniWebViewDownloadMatchingType.ExactValue) {
        #if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR_WIN && !UNITY_EDITOR_LINUX
        UniWebViewInterface.RemoveDownloadMIMETypes(listener.Name, MIMEType, (int)type);
        #endif
    }

    /// <summary>
    /// Sets whether allowing users to edit the file name before downloading. Default is `true`.
    ///
    /// If `true`, when a download task is triggered, a dialog will be shown to ask user to edit the file name and the
    /// user has a chance to choose if the they actually want to download the file. If `false`, the file download will
    /// start immediately without asking user to edit the file name. The file name is generated by guessing from the
    /// content disposition header and the MIME type. If the guessing of the file name fails, a random string will be
    /// used.
    ///
    /// </summary>
    /// <param name="allowed">
    /// Whether the user can edit the file name and determine whether actually starting the downloading.
    /// </param>
    public void SetAllowUserEditFileNameBeforeDownloading(bool allowed) {
        UniWebViewInterface.SetAllowUserEditFileNameBeforeDownloading(listener.Name, allowed);
    }

    /// <summary>
    /// Sets whether allowing users to choose the way to handle the downloaded file. Default is `true`.
    /// 
    /// On iOS, the downloaded file will be stored in a temporary folder. Setting this to `true` will show a system 
    /// default share sheet and give the user a chance to send and store the file to another location (such as the 
    /// File app or iCloud).
    /// 
    /// On macOS Editor, setting this to `true` will allow UniWebView to open the file in Finder.
    /// 
    /// This method does not have any effect on Android. On Android, the file is downloaded to the Download folder.
    /// 
    /// </summary>
    /// <param name="allowed">Whether the user can choose the way to handle the downloaded file.</param>
    public void SetAllowUserChooseActionAfterDownloading(bool allowed) {
        #if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR_WIN && !UNITY_EDITOR_LINUX
        UniWebViewInterface.SetAllowUserChooseActionAfterDownloading(listener.Name, allowed);
        #endif
    }

    /// <summary>
    /// Sets whether the `OnFileDownloadStarted` and `OnFileDownloadFinished` events should be raised even for an image
    /// saving action triggered by the callout (context) menu on Android.
    /// 
    /// By default, the image saving goes through a different route and it does not trigger the `OnFileDownloadStarted` 
    /// and `OnFileDownloadFinished` events like other normal download tasks. Setting this with enabled with `true` if
    /// you also need to get notified when user long-presses on the image and taps "Save Image" button. By default, the
    /// image will be saved to the Downloads directory and you can get the path from the parameter 
    /// of `OnFileDownloadFinished` event.
    /// 
    /// This only works on Android. On iOS, there is no way to get a callback or any event from the "Add to Photos"
    /// button in the callout menu.
    /// </summary>
    /// <param name="enabled">Whether the context menu image saving action triggers the download related events.</param>
    public void SetDownloadEventForContextMenuEnabled(bool enabled) {
        #if UNITY_ANDROID && !UNITY_EDITOR
        UniWebViewInterface.SetDownloadEventForContextMenuEnabled(listener.Name, enabled);
        #endif
    }

}