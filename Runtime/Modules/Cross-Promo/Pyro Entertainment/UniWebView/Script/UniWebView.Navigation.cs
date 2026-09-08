//
//  UniWebView.Navigation.cs
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
    /// The url of current loaded web page.
    /// </summary>
    public string Url => UniWebViewInterface.GetUrl(listener.Name);

    /// <summary>
    /// Loads a url in current web view.
    /// </summary>
    /// <param name="url">The url to be loaded. This url should start with `http://` or `https://` scheme. You could even load a non-ascii url text if it is valid.</param>
    /// <param name="skipEncoding">
    /// Whether UniWebView should skip encoding the url or not. If set to `false`, UniWebView will try to encode the url parameter before
    /// loading it. Otherwise, your original url string will be used as the url if it is valid. Default is `false`.
    /// </param>
    /// <param name="readAccessURL">
    /// The URL to allow read access to. This parameter is only used when loading from the filesystem in iOS, and passed
    /// to `loadFileURL:allowingReadAccessToURL:` method of WebKit. By default, the parent folder of the `url` parameter will be read accessible.
    /// </param>
    public void Load(string url, bool skipEncoding = false, string readAccessURL = null) {
        UniWebViewInterface.Load(listener.Name, url, skipEncoding, readAccessURL);
    }

    /// <summary>
    /// Loads an HTML string in current web view.
    /// </summary>
    /// <param name="htmlString">The HTML string to use as the contents of the webpage.</param>
    /// <param name="baseUrl">The url to use as the page's base url.</param>
    /// <param name="skipEncoding">
    /// Whether UniWebView should skip encoding the baseUrl or not. If set to `false`, UniWebView will try to encode the baseUrl parameter before
    /// using it. Otherwise, your original url string will be used as the baseUrl if it is valid. Default is `false`.
    /// </param>
    public void LoadHTMLString(string htmlString, string baseUrl, bool skipEncoding = false) {
        UniWebViewInterface.LoadHTMLString(listener.Name, htmlString, baseUrl, skipEncoding);
    }

    /// <summary>
    /// Reloads the current page.
    /// </summary>
    public void Reload() {
        UniWebViewInterface.Reload(listener.Name);
    }

    /// <summary>
    /// Stops loading all resources on the current page.
    /// </summary>
    public void Stop() {
        UniWebViewInterface.Stop(listener.Name);
    }

    /// <summary>
    /// Gets whether there is a back page in the back-forward list that can be navigated to.
    /// </summary>
    public bool CanGoBack => UniWebViewInterface.CanGoBack(listener.Name);

    /// <summary>
    /// Gets whether there is a forward page in the back-forward list that can be navigated to.
    /// </summary>
    public bool CanGoForward => UniWebViewInterface.CanGoForward(listener.Name);

    /// <summary>
    /// Navigates to the back item in the back-forward list.
    /// </summary>
    public void GoBack() {
        UniWebViewInterface.GoBack(listener.Name);
    }

    /// <summary>
    /// Navigates to the forward item in the back-forward list.
    /// </summary>
    public void GoForward() {
        UniWebViewInterface.GoForward(listener.Name);
    }

    /// <summary>
    /// Gets a copy of the back-forward list, which is the navigation history for the web view.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The back-forward list represents the browsing history in the current web view. It contains information
    /// about visited (both back and forward) pages and allows access to any entry in the history by index. This is a
    /// snapshot of the history at the time of calling - it won't update automatically with new navigation. Call
    /// `CopyBackForwardList` again to get the latest history if necessary.
    /// </para>
    /// </remarks>
    /// <returns>
    /// A <see cref="UniWebViewBackForwardList"/> object containing a snapshot of the navigation history.
    /// The list provides a read-only record of all web pages visited in this web view.
    /// </returns>
    public UniWebViewBackForwardList CopyBackForwardList() {
        var json = UniWebViewInterface.CopyBackForwardList(listener.Name);
        return new UniWebViewBackForwardList(json);
    }
    
    /// <summary>
    /// Navigates to the specified index in the back-forward list.
    ///
    /// The index is a zero-based index of the item in the back-forward list. If the index is out of range, this method
    /// does nothing.
    /// </summary>
    /// <param name="index">The zero-based index of the item in the back-forward list.</param>
    public void GoToIndexInBackForwardList(int index) {
        UniWebViewInterface.GoToIndexInBackForwardList(listener.Name, index);
    }
}