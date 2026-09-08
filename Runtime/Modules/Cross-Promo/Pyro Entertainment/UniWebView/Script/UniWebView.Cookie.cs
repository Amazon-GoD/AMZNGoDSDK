//
//  UniWebView.Cookie.cs
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
using System.Threading.Tasks;

public partial class UniWebView {
    /// <summary>
    /// Sets whether the UniWebView should allow third party cookies to be set. By default, on Android, the third party
    /// cookies are disallowed due to security reason. Setting this to `true` will allow the cookie manager to accept
    /// third party cookies you set. 
    /// 
    /// This method only works for Android. On iOS, this method does nothing because WKWebView fundamentally does not 
    /// support third party cookies. This is a deliberate architectural limitation of WKWebView for privacy and security 
    /// reasons. Third party cookies are always blocked on iOS and cannot be enabled through any API. If your application 
    /// requires third party cookie support, consider using alternative authentication methods such as OAuth or JWT tokens.
    /// 
    /// Note: Starting from iOS 18, the default SameSite cookie attribute changed from "None" to "Lax", which may affect 
    /// cross-site cookie behavior even for first party cookies.
    /// </summary>
    /// <param name="flag">Whether the third party cookies should be allowed.</param>
    public void SetAcceptThirdPartyCookies(bool flag) {
        #if UNITY_ANDROID && !UNITY_EDITOR
        UniWebViewInterface.SetAcceptThirdPartyCookies(listener.Name, flag);
        #endif
    }

    /// <summary>
    /// Clears all cookies from web view. When it finishes, the `handler` will be called.
    /// 
    /// This will clear cookies from all domains in the web view and previous.
    /// If you only need to remove cookies from a certain domain, use `RemoveCookies` instead.
    /// </summary>
    /// <param name="handler">An action that is called after the operation finishes.</param>
    public static void ClearCookies(Action handler) {
        var identifier = Guid.NewGuid().ToString();
        globalPayloadActions.Add(identifier, (payload) => {
            handler.Invoke();
        });
        UniWebViewInterface.ClearCookies(identifier);
    }

    /// <summary>
    /// Clears all cookies from web view asynchronously.
    ///
    /// This will clear cookies from all domains in the web view and previous.
    /// If you only need to remove cookies from a certain domain, use `RemoveCookies` instead.
    /// </summary>
    public static async Task ClearCookiesAsync() {
        var tcs = new TaskCompletionSource<object>();
        ClearCookies(() => {
            tcs.SetResult(null);
        });
        await tcs.Task;
    }

    /// <summary>
    /// Sets a cookie for a certain url. When it finishes, the `handler` will be called.
    /// </summary>
    /// <param name="url">The url to which cookie will be set.</param>
    /// <param name="cookie">The cookie string to set.</param>
    /// <param name="handler">An action that is called after the operation finishes.</param>
    public static void SetCookie(string url, string cookie, Action handler) {
        SetCookie(url, cookie, false, handler);
    }
    
    /// <summary>
    /// Sets a cookie for a certain url. When it finishes, the `handler` will be called.
    /// </summary>
    /// <param name="url">The url to which cookie will be set.</param>
    /// <param name="cookie">The cookie string to set.</param>
    /// <param name="skipEncoding">
    /// Whether UniWebView should skip encoding the url or not. If set to `false`, UniWebView will try to encode the url parameter before
    /// using it. Otherwise, your original url string will be used to set the cookie if it is valid.
    /// </param>
    /// <param name="handler">An action that is called after the operation finishes.</param>
    public static void SetCookie(string url, string cookie, bool skipEncoding, Action handler) {
        var identifier = Guid.NewGuid().ToString();
        globalPayloadActions.Add(identifier, (payload) => {
            handler.Invoke();
        });
        UniWebViewInterface.SetCookie(url, cookie, skipEncoding, identifier);
    }

    /// <summary>
    /// Sets a cookie for a certain url asynchronously.
    /// </summary>
    /// <param name="url">The url to which cookie will be set.</param>
    /// <param name="cookie">The cookie string to set.</param>
    /// <param name="skipEncoding">
    /// Whether UniWebView should skip encoding the url or not. If set to `false`, UniWebView will try to encode the url parameter before
    /// using it. Otherwise, your original url string will be used to set the cookie if it is valid. Default is `false`.
    /// </param>
    public static async Task SetCookieAsync(string url, string cookie, bool skipEncoding = false) {
        var tcs = new TaskCompletionSource<object>();
        SetCookie(url, cookie, skipEncoding, () => {
            tcs.SetResult(null);
        });
        await tcs.Task;
    }
    
    /// <summary>
    /// Gets the cookie value under a given url and key. When it finishes, the `handler` will be called with the cookie value
    /// if exists.
    /// </summary>
    /// <param name="url">The url (domain) where the target cookie is.</param>
    /// <param name="key">The key for target cookie value.</param>
    /// <param name="skipEncoding">
    /// Whether UniWebView should skip encoding the url or not. If set to `false`, UniWebView will try to encode the url parameter before
    /// using it. Otherwise, your original url string will be used to get the cookie if it is valid.
    /// </param>
    /// <param name="handler">An action that is called after the operation finishes.</param>
    public static void GetCookie(string url, string key, bool skipEncoding, Action<string> handler) {
        var identifier = Guid.NewGuid().ToString();
        globalPayloadActions.Add(identifier, (payload) => {
            handler.Invoke(payload.data);
        });
        UniWebViewInterface.GetCookie(url, key, skipEncoding, identifier);
    }
    
    /// <summary>
    /// Gets the cookie value under a given url and key asynchronously.
    ///
    /// </summary>
    /// <param name="url">The url (domain) where the target cookie is.</param>
    /// <param name="key">The key for target cookie value.</param>
    /// <param name="skipEncoding">
    /// Whether UniWebView should skip encoding the url or not. If set to `false`, UniWebView will try to encode the url parameter before
    /// using it. Otherwise, your original url string will be used to get the cookie if it is valid. Default is `false`.
    /// </param>
    public static async Task<string> GetCookieAsync(string url, string cookieName, bool skipEncoding = false) {
        var tcs = new TaskCompletionSource<string>();
        GetCookie(url, cookieName, skipEncoding, (result) =>  {
            tcs.SetResult(result);
        });
        return await tcs.Task;
    }

    /// <summary>
    /// Removes all the cookies under a given url. When it finishes, the `handler` will be called.
    /// </summary>
    /// <param name="url">The url (domain) where the cookies are under.</param>
    /// <param name="handler">An action that is called after the operation finishes.</param>
    public static void RemoveCookies(string url, Action handler) {
        RemoveCookies(url, false, handler);
    }
    
    /// <summary>
    /// Removes all the cookies under a given url. When it finishes, the `handler` will be called.
    /// </summary>
    /// <param name="url">The url (domain) where the cookies are under.</param>
    /// <param name="handler">An action that is called after the operation finishes.</param>
    /// <param name="skipEncoding">
    /// Whether UniWebView should skip encoding the url or not. If set to `false`, UniWebView will try to encode the url parameter before
    /// using it. Otherwise, your original url string will be used to get the cookie if it is valid.
    /// </param>
    public static void RemoveCookies(string url, bool skipEncoding, Action handler) {
        var identifier = Guid.NewGuid().ToString();
        globalPayloadActions.Add(identifier, (payload) => {
            handler.Invoke();
        });
        UniWebViewInterface.RemoveCookies(url, skipEncoding, identifier);
    }
    
    /// <summary>
    /// Removes all the cookies under a given url asynchronously.
    /// </summary>
    /// <param name="url">The url (domain) where the cookies are under.</param>
    /// <param name="handler">An action that is called after the operation finishes.</param>
    /// <param name="skipEncoding">
    /// Whether UniWebView should skip encoding the url or not. If set to `false`, UniWebView will try to encode the url parameter before
    /// using it. Otherwise, your original url string will be used to get the cookie if it is valid. Default is `false`.
    /// </param>
    public static async Task RemoveCookiesAsync(string url, bool skipEncoding = false) {
        var tcs = new TaskCompletionSource<object>();
        RemoveCookies(url, skipEncoding, () => {
            tcs.SetResult(null);
        });
        await tcs.Task;
    }

    /// <summary>
    /// Removes a certain cookie under the given url for the specified key. When it finishes, the `handler` will be called.
    /// </summary>
    /// <param name="url">The url (domain) where the cookies are under.</param>
    /// <param name="key">The key for target cookie.</param>
    /// <param name="handler">An action that is called after the operation finishes.</param>
    public static void RemoveCookie(string url, string key, Action handler) {
        RemoveCookie(url, key, false, handler);
    }
    
    /// <summary>
    /// Removes a certain cookie under the given url for the specified key. When it finishes, the `handler` will be called.
    /// </summary>
    /// <param name="url">The url (domain) where the cookies are under.</param>
    /// <param name="key">The key for target cookie.</param>
    /// <param name="skipEncoding">
    /// Whether UniWebView should skip encoding the url or not. If set to `false`, UniWebView will try to encode the url parameter before
    /// using it. Otherwise, your original url string will be used to get the cookie if it is valid.
    /// </param>
    /// <param name="handler">An action that is called after the operation finishes.</param>
    public static void RemoveCookie(string url, string key, bool skipEncoding, Action handler) {
        var identifier = Guid.NewGuid().ToString();
        globalPayloadActions.Add(identifier, (payload) => {
            handler.Invoke();
        });
        UniWebViewInterface.RemoveCookie(url, key, skipEncoding, identifier);
    }
    
    /// <summary>
    /// Removes a certain cookie under the given url for the specified key. asynchronously 
    /// </summary>
    /// <param name="url">The url (domain) where the cookies are under.</param>
    /// <param name="key">The key for target cookie.</param>
    /// <param name="skipEncoding">
    /// Whether UniWebView should skip encoding the url or not. If set to `false`, UniWebView will try to encode the url parameter before
    /// using it. Otherwise, your original url string will be used to get the cookie if it is valid. Default is `false`.
    /// </param>
    public static async Task RemoveCookieAsync(string url, string key, bool skipEncoding = false) {
        var tcs = new TaskCompletionSource<object>();
        RemoveCookie(url, key, skipEncoding, () => {
            tcs.SetResult(null);
        });
        await tcs.Task;
    }
}