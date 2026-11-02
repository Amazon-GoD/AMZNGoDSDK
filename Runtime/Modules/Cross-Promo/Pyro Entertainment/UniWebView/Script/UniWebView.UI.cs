//
//  UniWebView.UI.cs
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

public partial class UniWebView {
    /// <summary>
    /// Represents the embedded toolbar in the current web view.
    /// </summary>
    public UniWebViewEmbeddedToolbar EmbeddedToolbar { get; private set; }

    /// <summary>
    /// Gets or sets the frame of current web view. The value is based on current `Screen.width` and `Screen.height`.
    /// The first two values of `Rect` is `x` and `y` position and the followed two `width` and `height`.
    /// </summary>
    public Rect Frame {
        get => frame;
        set {
            frame = value;
            UpdateFrame();
        }
    }

    /// <summary>
    /// A reference rect transform which the web view should change its position and size to.
    /// Set it to a Unity UI element (which contains a `RectTransform`) under a canvas to determine 
    /// the web view frame by a certain UI element. 
    /// 
    /// By using this, you could get benefit from [Multiple Resolutions UI](https://docs.unity3d.com/Manual/HOWTO-UIMultiResolution.html).
    /// 
    /// </summary>
    public RectTransform ReferenceRectTransform {
        get => referenceRectTransform;
        set {
            referenceRectTransform = value;
            UpdateFrame();
        }
    }

    /// <summary>
    /// Updates and sets current frame of web view to match the setting.
    /// 
    /// This is useful if the `referenceRectTransform` is changed and you need to sync the frame change
    /// to the web view. This method follows the frame determining rules.
    /// </summary>
    public void UpdateFrame() {
        Rect rect = NextFrameRect();
        // Sync web view frame property.
        frame = rect;
        UniWebViewInterface.SetFrame(listener.Name, (int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height);
    }
    
    Rect UnmappedFrame() {
        return referenceRectTransform != null ? referenceRectTransform.rect : frame;
    }
    
    Rect NextFrameRect() {
        if (referenceRectTransform == null) {
            UniWebViewLogger.Instance.Info("Using Frame setting to determine web view frame.");
            return frame;
        }

        UniWebViewLogger.Instance.Info("Using reference RectTransform to determine web view frame.");
        var worldCorners = new Vector3[4];
            
        referenceRectTransform.GetWorldCorners(worldCorners);
            
        // var bottomLeft = worldCorners[0];
        var topLeft = worldCorners[1];
        // var topRight = worldCorners[2];
        var bottomRight = worldCorners[3];

        var canvas = referenceRectTransform.GetComponentInParent<Canvas>();
        if (canvas == null) {
            return frame;
        }

        switch (canvas.renderMode) {
            case RenderMode.ScreenSpaceOverlay:
                break;
            case RenderMode.ScreenSpaceCamera:
            case RenderMode.WorldSpace:
                var camera = canvas.worldCamera;
                if (camera == null) {
                    UniWebViewLogger.Instance.Critical(@"You need a render camera 
                        or event camera to use RectTransform to determine correct 
                        frame for UniWebView.");
                    UniWebViewLogger.Instance.Info("No camera found. Fall back to ScreenSpaceOverlay mode.");
                } else {
                    // bottomLeft = camera.WorldToScreenPoint(bottomLeft);
                    topLeft = camera.WorldToScreenPoint(topLeft);
                    // topRight = camera.WorldToScreenPoint(topRight);
                    bottomRight = camera.WorldToScreenPoint(bottomRight);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        var nativeHeight = UniWebViewInterface.NativeScreenHeight();
        var statusBarHeight = GetStatusBarHeight();
        
        // Calculate scaling factors to convert Unity coordinates to native screen coordinates
        // - When "Render Outside Safe Area" is disabled, Unity's coordinate space doesn't include the status bar area
        // - But native WebView positioning needs to account for the status bar offset
        // - This creates a mismatch that needs correction in the Y coordinate calculation
        var widthFactor = UniWebViewInterface.NativeScreenWidth() / Screen.width;
        var heightFactor = (nativeHeight - statusBarHeight) / Screen.height;
        
        // Calculate status bar offset in Unity coordinate space
        // This is used to adjust Y positioning when status bar is visible
        var safeAreaOffsetY = statusBarHeight / heightFactor;
        
        // Convert Unity RectTransform coordinates to native screen coordinates
        // 
        // Unity coordinate system:
        // - Origin (0,0) is at bottom-left of screen
        // - Y increases upward
        // 
        // Native coordinate system:
        // - Origin (0,0) is at top-left of screen (considering status bar)
        // - Y increases downward
        // 
        // The calculation process:
        // 1. topLeft.x * widthFactor: Convert Unity X to native X
        // 2. (Screen.height - topLeft.y + safeAreaOffsetY) * heightFactor: 
        //    - Screen.height - topLeft.y: Flip Y coordinate (Unity bottom-left to top-left)
        //    - + safeAreaOffsetY: Adjust for status bar offset when it's visible
        //    - * heightFactor: Scale to native screen height (If the resolution is different from native size)
        var x = topLeft.x * widthFactor;
        var y = (Screen.height - topLeft.y + safeAreaOffsetY) * heightFactor;
        var width = (bottomRight.x - topLeft.x) * widthFactor;
        var height = (topLeft.y - bottomRight.y) * heightFactor;
        return new Rect(x, y, width, height);
    }

    /// <summary>
    /// Applies a transformation to the web view, including rotation and scaling.
    ///
    /// This method allows you to rotate and scale the web view by specifying a <see cref="UniWebViewTransform"/>
    /// object. The transformation is applied relative to the web view's current position and size, with its center
    /// as the anchor.
    ///
    /// The transformation includes:
    /// - **Rotation**: The angle (in degrees) by which the web view should be rotated. Positive values rotate the web
    /// view clockwise, while negative values rotate it counterclockwise.
    /// - **Scaling**: The scale factors for the X and Y axes. A scale factor of 1.0 means no scaling, while values
    /// greater than 1.0 enlarge the web view and values less than 1.0 shrink it.
    ///
    /// This method works on iOS and Android. It does nothing in Unity Editor on macOS.
    /// 
    /// </summary>
    /// <param name="transform">
    /// A <see cref="UniWebViewTransform"/> object containing the rotation angle and scale factors to apply to the web
    /// view.
    /// </param>
    public void SetTransform(UniWebViewTransform transform) {
        UniWebViewInterface.SetTransform(listener.Name, transform.Rotation, transform.ScaleX, transform.ScaleY);
    }

    /// <summary>
    /// Gets the current round corner radius applied to this web view.
    /// </summary>
    public UniWebViewCornerRadius CornerRadius => cornerRadius;

    /// <summary>
    /// Sets the same round corner radius value for all corners.
    /// </summary>
    /// <param name="radius">The radius applied to every corner.</param>
    public void SetRoundCornerRadius(float radius) {
        SetRoundCornerRadius(UniWebViewCornerRadius.Uniform(radius));
    }

    /// <summary>
    /// Sets round corner radius values for each individual corner.
    /// </summary>
    /// <param name="topLeft">Radius at the top-left corner.</param>
    /// <param name="topRight">Radius at the top-right corner.</param>
    /// <param name="bottomLeft">Radius at the bottom-left corner.</param>
    /// <param name="bottomRight">Radius at the bottom-right corner.</param>
    public void SetRoundCornerRadius(
        float topLeft,
        float topRight,
        float bottomLeft,
        float bottomRight
    ) {
        SetRoundCornerRadius(new UniWebViewCornerRadius(topLeft, topRight, bottomLeft, bottomRight));
    }

    /// <summary>
    /// Applies the provided round corner radius configuration to the native web view.
    /// </summary>
    /// <param name="radius">The corner radius definition.</param>
    public void SetRoundCornerRadius(UniWebViewCornerRadius radius) {
        cornerRadius = radius;
        UniWebViewInterface.SetRoundCornerRadius(
            listener.Name,
            radius.TopLeft,
            radius.TopRight,
            radius.BottomLeft,
            radius.BottomRight
        );
    }

    /// <summary>
    /// Sets the web view visible on screen.
    /// 
    /// If you pass `false` and `UniWebViewTransitionEdge.None` to the first two parameters, it means no animation will
    /// be applied when showing. So the `duration` parameter will not be taken into account. Otherwise, when 
    /// either or both `fade` and `edge` set, the showing operation will be animated.
    /// 
    /// Regardless of there is an animation or not, the `completionHandler` will be called if not `null` when the web 
    /// view showing finishes.
    /// </summary>
    /// <param name="fade">Whether show with a fade in animation. Default is `false`.</param>
    /// <param name="edge">The edge from which the web view showing. It simulates a modal effect when showing a web view. Default is `UniWebViewTransitionEdge.None`.</param>
    /// <param name="duration">Duration of the showing animation. Default is `0.4f`.</param>
    /// <param name="completionHandler">Completion handler which will be called when showing finishes. Default is `null`.</param>
    /// <returns>A bool value indicates whether the showing operation started.</returns>
    public bool Show(bool fade = false, UniWebViewTransitionEdge edge = UniWebViewTransitionEdge.None, 
                float duration = 0.4f, Action completionHandler = null) 
    {
        return _Show(fade, edge, duration, false, completionHandler);
    }

    public bool _Show(bool fade = false, UniWebViewTransitionEdge edge = UniWebViewTransitionEdge.None, 
                float duration = 0.4f, bool useAsync = false, Action completionHandler = null) 
    {
        var identifier = Guid.NewGuid().ToString();
        var showStarted = UniWebViewInterface.Show(listener.Name, fade, (int)edge, duration, useAsync, identifier);
        if (showStarted && completionHandler != null) {
            var hasAnimation = (fade || edge != UniWebViewTransitionEdge.None);
            if (hasAnimation) {
                actions.Add(identifier, completionHandler);
            } else {
                completionHandler();
            }
        }
        
#pragma warning disable 618
        if (showStarted && useToolbar) {
            var top = (toolbarPosition == UniWebViewToolbarPosition.Top);
            SetShowToolbar(true, false, top, fullScreen);
        }
#pragma warning restore 618
        return showStarted;
    }

    /// <summary>
    /// Sets the web view invisible from screen.
    /// 
    /// If you pass `false` and `UniWebViewTransitionEdge.None` to the first two parameters, it means no animation will 
    /// be applied when hiding. So the `duration` parameter will not be taken into account. Otherwise, when either or 
    /// both `fade` and `edge` set, the hiding operation will be animated.
    /// 
    /// Regardless there is an animation or not, the `completionHandler` will be called if not `null` when the web view
    /// hiding finishes.
    /// </summary>
    /// <param name="fade">Whether hide with a fade in animation. Default is `false`.</param>
    /// <param name="edge">The edge from which the web view hiding. It simulates a modal effect when hiding a web view. Default is `UniWebViewTransitionEdge.None`.</param>
    /// <param name="duration">Duration of hiding animation. Default is `0.4f`.</param>
    /// <param name="completionHandler">Completion handler which will be called when hiding finishes. Default is `null`.</param>
    /// <returns>A bool value indicates whether the hiding operation started.</returns>
    public bool Hide(bool fade = false, UniWebViewTransitionEdge edge = UniWebViewTransitionEdge.None,
                float duration = 0.4f, Action completionHandler = null)
    {
        return _Hide(fade, edge, duration, false, completionHandler);
    }

    public bool _Hide(bool fade = false, UniWebViewTransitionEdge edge = UniWebViewTransitionEdge.None,
                float duration = 0.4f, bool useAsync = false, Action completionHandler = null)
    {
        var identifier = Guid.NewGuid().ToString();
        var hideStarted = UniWebViewInterface.Hide(listener.Name, fade, (int)edge, duration, useAsync, identifier);
        if (hideStarted && completionHandler != null) {
            var hasAnimation = (fade || edge != UniWebViewTransitionEdge.None);
            if (hasAnimation) {
                actions.Add(identifier, completionHandler);
            } else {
                completionHandler();
            }
        }
#pragma warning disable 618
        if (hideStarted && useToolbar) {
            var top = (toolbarPosition == UniWebViewToolbarPosition.Top);
            SetShowToolbar(false, false, top, fullScreen);
        }
#pragma warning restore 618
        return hideStarted;
    }

    /// <summary>
    /// Animates the web view from current position and size to another position and size.
    /// </summary>
    /// <param name="frame">The new `Frame` which the web view should be.</param>
    /// <param name="duration">Duration of the animation.</param>
    /// <param name="delay">Delay before the animation begins. Default is `0.0f`, which means the animation will start immediately.</param>
    /// <param name="completionHandler">Completion handler which will be called when animation finishes. Default is `null`.</param>
    /// <returns></returns>
    public bool AnimateTo(Rect frame, float duration, float delay = 0.0f, Action completionHandler = null) {
        var identifier = Guid.NewGuid().ToString();
        var animationStarted = UniWebViewInterface.AnimateTo(listener.Name, 
                    (int)frame.x, (int)frame.y, (int)frame.width, (int)frame.height, duration, delay, identifier);
        if (animationStarted) {
            this.frame = frame;
            if (completionHandler != null) {
                actions.Add(identifier, completionHandler);
            }
        }
        return animationStarted;
    }

    /// <summary>
    /// Sets the adjustment behavior which indicates how safe area insets 
    /// are added to the adjusted content inset. It is a wrapper of `contentInsetAdjustmentBehavior` on iOS.
    /// 
    /// It only works on iOS 11 and above. You need to call this method as soon as you create a web view,
    /// before you call any other methods related to web view layout (like `Show` or `SetShowToolbar`).
    /// </summary>
    /// <param name="behavior">The behavior for determining the adjusted content offsets.</param>
    public void SetContentInsetAdjustmentBehavior(
        UniWebViewContentInsetAdjustmentBehavior behavior
    )
    {
        #if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR_WIN && !UNITY_EDITOR_LINUX
        UniWebViewInterface.SetContentInsetAdjustmentBehavior(listener.Name, behavior);
        #endif
    }

    /// <summary>
    /// Sets whether the web view area should avoid soft keyboard. If `true`, when the keyboard shows up, the web views
    /// content view will resize itself to avoid keyboard overlap the web content. Otherwise, the web view will not resize
    /// and just leave the content below under the soft keyboard.
    /// 
    /// This method is only for Android. On iOS, the keyboard avoidance is built into the system directly and there is 
    /// no way to change its behavior.
    /// 
    /// Default is `true`.
    /// </summary>
    /// <param name="flag">Whether the keyboard should avoid web view content.</param>
    public static void SetEnableKeyboardAvoidance(bool flag) {
        #if UNITY_ANDROID && !UNITY_EDITOR
        UniWebViewInterface.SetEnableKeyboardAvoidance(flag);
        #endif
    }

    private Color backgroundColor = Color.white;
    /// <summary>
    /// Gets or sets the background color of web view. The default value is `Color.white`.
    /// </summary>
    public Color BackgroundColor {
        get => backgroundColor;
        set {
            backgroundColor = value;
            UniWebViewInterface.SetBackgroundColor(listener.Name, value.r, value.g, value.b, value.a);
        }
    }

    /// <summary>
    /// Gets or sets the alpha value of the whole web view.
    /// 
    /// You can make the game scene behind web view visible to make the web view transparent.
    /// 
    /// Default is `1.0f`, which means totally opaque. Set it to `0.0f` will make the web view totally transparent.
    /// </summary>
    public float Alpha {
        get => UniWebViewInterface.GetWebViewAlpha(listener.Name);
        set => UniWebViewInterface.SetWebViewAlpha(listener.Name, value);
    }


    /// <summary>
    /// Sets whether to show a loading indicator while the loading is in progress.
    /// </summary>
    /// <param name="flag">Whether an indicator should show.</param>
    public void SetShowSpinnerWhileLoading(bool flag) {
        UniWebViewInterface.SetShowSpinnerWhileLoading(listener.Name, flag);
    }

    /// <summary>
    /// Sets the text displayed in the loading indicator, if `SetShowSpinnerWhileLoading` is set to `true`.
    /// </summary>
    /// <param name="text">The text to display while loading indicator visible. Default is "Loading..."</param>
    public void SetSpinnerText(string text) {
        UniWebViewInterface.SetSpinnerText(listener.Name, text);
    }

    /// <summary>
    /// Sets whether the user can dismiss the loading indicator by tapping on it or the greyed-out background around.
    ///
    /// By default, when the loading spinner is shown, the user can dismiss it by a single tapping. Call this method
    /// with `false` to disable this behavior.
    /// </summary>
    /// <param name="flag">Whether the user can dismiss the loading indicator.</param>
    public void SetAllowUserDismissSpinner(bool flag) {
        UniWebViewInterface.SetAllowUserDismissSpinnerByGesture(listener.Name, flag);
    }

    /// <summary>
    /// Shows the loading spinner.
    ///
    /// Calling this method will show the loading spinner, regardless if the `SetShowSpinnerWhileLoading` is set to
    /// `true` or `false`. However, if `SetShowSpinnerWhileLoading` was called with `true`, UniWebView will still hide
    /// the spinner when the loading finishes.
    /// </summary>
    public void ShowSpinner() {
        UniWebViewInterface.ShowSpinner(listener.Name);
    }

    /// <summary>
    /// Hides the loading spinner.
    ///
    /// Calling this method will hide the loading spinner, regardless if the `SetShowSpinnerWhileLoading` is set to
    /// `true` or `false`. However, if `SetShowSpinnerWhileLoading` was called with `false`, UniWebView will still show
    /// the spinner when the loading starts.
    /// </summary>
    public void HideSpinner() {
        UniWebViewInterface.HideSpinner(listener.Name);
    }

    /// <summary>
    /// Sets whether the horizontal scroll bar should show when the web content beyonds web view bounds.
    /// 
    /// This only works on mobile platforms. It will do nothing on macOS Editor.
    /// </summary>
    /// <param name="enabled">Whether enable the scroll bar or not.</param>
    public void SetHorizontalScrollBarEnabled(bool enabled) {
        UniWebViewInterface.SetHorizontalScrollBarEnabled(listener.Name, enabled);
    }

    /// <summary>
    /// Sets whether the vertical scroll bar should show when the web content beyonds web view bounds.
    /// 
    /// This only works on mobile platforms. It will do nothing on macOS Editor.
    /// </summary>
    /// <param name="enabled">Whether enable the scroll bar or not.</param>
    public void SetVerticalScrollBarEnabled(bool enabled) {
        UniWebViewInterface.SetVerticalScrollBarEnabled(listener.Name, enabled);
    }

    /// <summary>
    /// Sets whether the web view should show with a bounces effect when scrolling to page edge.
    /// 
    /// This only works on mobile platforms. It will do nothing on macOS Editor.
    /// </summary>
    /// <param name="enabled">Whether the bounces effect should be applied or not.</param>
    public void SetBouncesEnabled(bool enabled) {
        UniWebViewInterface.SetBouncesEnabled(listener.Name, enabled);
    }

    /// <summary>
    /// Sets whether the web view supports zoom gesture to change content size. 
    /// Default is `false`, which means the zoom gesture is not supported.
    /// </summary>
    /// <param name="enabled">Whether the zoom gesture is allowed or not.</param>
    public void SetZoomEnabled(bool enabled) {
        UniWebViewInterface.SetZoomEnabled(listener.Name, enabled);
    }

    /// <summary>
    /// Sets whether the web view can receive user interaction or not.
    /// 
    /// By setting this to `false`, the web view will not accept any user touch event so your users cannot tap links or
    /// scroll the page.
    /// 
    /// </summary>
    /// <param name="enabled">Whether the user interaction should be enabled or not.</param>
    public void SetUserInteractionEnabled(bool enabled) {
        UniWebViewInterface.SetUserInteractionEnabled(listener.Name, enabled);
    }

    /// <summary>
    /// Sets whether the web view should pass through clicks at clear pixels to Unity scene.
    /// 
    /// Setting this method is a pre-condition for the whole passing-through feature to work. To allow your touch passing through
    /// to Unity scene, the following conditions should be met at the same time:
    /// 
    ///     1. This method is called with `true` and the web view accepts passing-through clicks.
    ///     2. The web view has a transparent background in body style for its content by CSS.
    ///     3. The web view itself has a transparent background color by setting `BackgroundColor` with a clear color.
    /// 
    /// Then, when user clicks on the clear pixel on the web view, the touch events will not be handled by the web view.
    /// Instead, these events are passed to Unity scene. By using this feature, it is possible to create a native UI with the 
    /// web view. 
    /// 
    /// Only clicks on transparent part on the web view will be delivered to Unity scene. The web view still intercepts
    /// and handles other touches on visible pixels on the web view.
    /// </summary>
    /// <param name="enabled">Whether the transparency clicking through feature should be enabled in this web view.</param>
    public void SetTransparencyClickingThroughEnabled(bool enabled) {
        UniWebViewInterface.SetTransparencyClickingThroughEnabled(listener.Name, enabled);
    }

    /// <summary>
    /// Refreshes the transparency clicking through layout.
    ///
    /// Call this method when the web page content changes dynamically without triggering DOM mutations that the
    /// automatic detection can observe. This manually triggers a re-collection of opaque regions marked with
    /// `data-uv-transparency="opaque"` attributes.
    ///
    /// This is useful when:
    /// - Content is updated programmatically without DOM changes
    /// - Canvas or other dynamic rendering changes opacity
    /// - You need to ensure the transparency regions are up-to-date
    ///
    /// This method only works when transparency clicking through is enabled. If the feature is not enabled,
    /// calling this method does nothing.
    /// </summary>
    /// <remarks>
    /// Available on both iOS and Android. Each platform refreshes the DOM-provided transparency mask immediately.
    /// </remarks>
    public void RefreshTransparencyClickingThroughLayout() {
        UniWebViewInterface.RefreshTransparencyClickingThroughLayout(listener.Name);
    }

    /// <summary>
    /// Enables user resizing for web view window. By default, you can only set the window size
    /// by setting its frame on mac Editor. By enabling user resizing, you would be able to resize 
    /// the window by dragging its border as a normal macOS window.
    /// 
    /// This method only works for macOS for debugging purpose. It does nothing on iOS and Android.
    /// </summary>
    /// <param name="enabled">Whether the window could be able to be resized by cursor.</param>
    public void SetWindowUserResizeEnabled(bool enabled) {
        #if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR_WIN && !UNITY_EDITOR_LINUX
        UniWebViewInterface.SetWindowUserResizeEnabled(listener.Name, enabled);
        #endif
    }

    /// <summary>
    /// Sets whether horizontal swipe gestures should trigger back-forward list navigation.
    /// 
    /// By setting with `true`, users can swipe from screen edge to perform a back or forward navigation.
    /// This method only works on iOS and macOS Editor. Default is `false`. 
    /// 
    /// On Android, the screen navigation gestures are simulating the traditional back button and it is enabled by 
    /// default. To disable gesture navigation on Android, you have to also disable the device back button. See 
    /// `SetBackButtonEnabled` for that purpose.
    /// </summary>
    /// <param name="flag">
    /// The value indicates whether a swipe gestures driven navigation should be allowed. Default is `false`.
    /// </param>
    public void SetAllowBackForwardNavigationGestures(bool flag) {
        #if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR_WIN && !UNITY_EDITOR_LINUX
        UniWebViewInterface.SetAllowBackForwardNavigationGestures(listener.Name, flag);
        #endif
    }

    /// <summary>
    /// Sets the default font size used in the web view.
    /// 
    /// On Android, the web view font size can be affected by the system font scale setting. Use this method to set the 
    /// font size in a more reasonable way, by giving the web view another default font size with the system font scale 
    /// considered. It can removes or reduces the effect of system font scale when displaying the web content.
    /// 
    /// This method only works on Android. On iOS, this method does nothing since the web view will respect the font 
    /// size setting in your CSS styles.
    /// </summary>
    /// <param name="size">The target default font size set to the web view.</param>
    public void SetDefaultFontSize(int size) {
        #if UNITY_ANDROID && !UNITY_EDITOR
        UniWebViewInterface.SetDefaultFontSize(listener.Name, size);
        #endif
    }

    /// <summary>
    /// Sets the text zoom used in the web view.
    /// 
    /// On Android, this method call `WebSettings.setTextZoom` to the the text zoom used in the web view.
    /// 
    /// This method only works on Android.
    /// </summary>
    /// <param name="textZoom">The text zoom in percent.</param>
    public void SetTextZoom(int textZoom) {
        #if UNITY_ANDROID && !UNITY_EDITOR
        UniWebViewInterface.SetTextZoom(listener.Name, textZoom);
        #endif
    }

    /// <summary>
    /// Scrolls the web view to a certain point.
    /// 
    /// Use 0 for both `x` and `y` value to scroll the web view to its origin.
    /// In a normal vertical web page, it is equivalent as scrolling to top.
    /// 
    /// You can use the `animated` parameter to control whether scrolling the page with or without animation.
    /// This parameter only works on iOS and Android. On macOS editor, the scrolling always happens without animation.
    /// </summary>
    /// <param name="x">X value of the target scrolling point.</param>
    /// <param name="y">Y value of the target scrolling point.</param>
    /// <param name="animated">If `true`, the scrolling happens with animation. Otherwise, it happens without
    ///  animation and the content is set directly.
    /// </param>
    public void ScrollTo(int x, int y, bool animated) {
        UniWebViewInterface.ScrollTo(listener.Name, x, y, animated);
    }
}
