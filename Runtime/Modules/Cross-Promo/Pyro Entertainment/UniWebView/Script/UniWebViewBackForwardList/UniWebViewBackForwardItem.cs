using System.Collections.Generic;

/// <summary>
/// Represents an item in the back-forward navigation list of a UniWebView browser.
/// This class stores information about a single entry in the browsing history.
///
/// You do not create an instance of this class directly. Instead, you get instances of this class from the
/// `UniWebViewBackForwardList` class.
/// </summary>
public class UniWebViewBackForwardItem {
    /// <summary>
    /// Gets the current URL of the history item.
    /// </summary>
    public string Url { get; private set; }

    /// <summary>
    /// Gets the title of the webpage represented by this history item.
    ///
    /// Empty if the title is not available or set.
    /// </summary>
    public string Title { get; private set; }

    /// <summary>
    /// Gets the original URL before any redirects occurred.
    /// </summary>
    public string OriginalUrl { get; private set; }

    /// <summary>
    /// Initializes a new instance of UniWebViewBackForwardItem.
    /// </summary>
    /// <param name="dict">A dictionary containing the item's URL, title and original URL information.</param>
    public UniWebViewBackForwardItem(Dictionary<string, object> dict) {
        Url = dict["url"] as string;
        Title = dict["title"] as string;
        OriginalUrl = dict["originalUrl"] as string;
    }
}
