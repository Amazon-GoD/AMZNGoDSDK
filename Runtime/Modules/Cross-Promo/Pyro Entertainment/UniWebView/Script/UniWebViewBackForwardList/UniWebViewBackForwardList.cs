using System.Collections.Generic;
using UniWebViewExternal;

/// <summary>
/// Represents the back-forward navigation history list of a UniWebView instance.
/// This class manages the browsing history and provides access to previous and next pages.
///
/// You do not create an instance of this class directly. Instead, you get an instance of this class to represent the
/// back-forward list of a UniWebView instance by calling the `CopyBackForwardList` method of the `UniWebView` class.
///
/// The content of this class is read-only and fixed when the instance is created. It does not get updated with the
/// web view's navigation history automatically. If you need the latest navigation history, you should call the method
/// `CopyBackForwardList` again to get a new instance of this class.
/// </summary>
public class UniWebViewBackForwardList {
    
    private readonly List<UniWebViewBackForwardItem> items;
    private readonly int currentIndex;
    
    public UniWebViewBackForwardList(string json) {
        var dict = Json.Deserialize(json) as Dictionary<string, object>;
        
        if (dict.TryGetValue("currentIndex", out object currentIndexObj) && 
            int.TryParse(currentIndexObj.ToString(), out int parsedIndex))
        {
            currentIndex = parsedIndex;
        }

        items = new List<UniWebViewBackForwardItem>();
        if (!(dict["items"] is List<object>)) {
            UniWebViewLogger.Instance.Critical("Invalid items in back-forward list: " + dict["items"]);
            return;
        }
        var itemsArray = (List<object>)dict["items"];
        foreach (var item in itemsArray) {
            if (item is Dictionary<string, object> itemDict) {
                items.Add(new UniWebViewBackForwardItem(itemDict));
            } else {
                UniWebViewLogger.Instance.Critical("Invalid item in back-forward list: " + item);
            }
        }
    }

    /// <summary>
    /// Gets all items in the back-forward navigation history list.
    /// </summary>
    public List<UniWebViewBackForwardItem> AllItems => items;

    /// <summary>
    /// Gets the current page item in the navigation history.
    ///
    /// It is the page that is currently displayed in the list. If there is no item in the list, it will return null.
    /// </summary>
    public UniWebViewBackForwardItem CurrentItem => ItemAtIndex(currentIndex);

    /// <summary>
    /// Gets the previous (back) page item in the navigation history.
    /// 
    /// Returns null if there is no previous page.
    /// </summary>
    public UniWebViewBackForwardItem BackItem => ItemAtIndex(currentIndex - 1);

    /// <summary>
    /// Gets the next (forward) page item in the navigation history.
    /// 
    /// Returns null if there is no next page.
    /// </summary>
    public UniWebViewBackForwardItem ForwardItem => ItemAtIndex(currentIndex + 1);

    /// <summary>
    /// Gets the index of current page in the navigation history.
    ///
    /// The index is zero-based in the list. If there is no item in the list, it will return -1.
    /// </summary>
    public int CurrentIndex => currentIndex;

    /// <summary>
    /// Gets the item at the specified index in the navigation history.
    /// </summary>
    /// <param name="index">The zero-based index of the item to retrieve.</param>
    /// <returns>The navigation item at the specified index, or null if the index is out of range.</returns>
    public UniWebViewBackForwardItem ItemAtIndex(int index) {
        if (index >= 0 && index < items.Count) {
            return items[index];
        }
        return null;
    }

    /// <summary>
    /// Gets the total number of items in the navigation history.
    /// </summary>
    public int Size => items.Count;
}